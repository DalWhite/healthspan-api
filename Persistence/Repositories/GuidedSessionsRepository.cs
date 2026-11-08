#nullable enable
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services;

/// <summary>
/// Data access for guided sessions. A guided session is today's Workout carrying
/// a frozen GuidedSnapshotJson; uniqueness per user/day is the DailySessionKey
/// unique index. No separate session table.
/// </summary>
public class GuidedSessionsRepository(MainDbContext context)
{
    /// <summary>
    /// Process-level per-key locks so concurrent from-plan calls for the same
    /// user/day serialize even on providers without real transaction isolation
    /// (InMemory tests). The DB unique index is the cross-process backstop.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DailyKeyLocks = new();

    private readonly MainDbContext _context = context;

    /// <summary>
    /// Idempotent create-or-resume keyed by DailySessionKey (user+day).
    /// Never reopens a completed session and never creates a second session
    /// for the day.
    /// </summary>
    public async Task<(GuidedSessionFromPlanResult Result, Workout Session)> CreateOrResumeFromPlanAsync(
        string userId,
        DateOnly date,
        string templateType,
        Guid clientSessionId,
        string snapshotJson,
        CancellationToken cancellationToken = default)
    {
        var key = WorkoutDailySessionKey.Create(userId, date);
        var gate = DailyKeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        IDbContextTransaction transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    cancellationToken);
            }

            var existing = await FindTodaySessionAsync(userId, date, cancellationToken);

            if (existing is not null)
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return (ClassifyExisting(existing, snapshotJson), existing);
            }

            var session = new Workout
            {
                Id = Guid.NewGuid(),
                ClientEntityId = clientSessionId,
                UserId = userId,
                Date = date.ToDateTime(TimeOnly.MinValue),
                Type = templateType,
                DailySessionKey = key,
                GuidedSnapshotJson = snapshotJson,
            };
            _context.Workouts.Add(session);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Cross-process race on the DailySessionKey unique index: the
                // other insert won; adopt it.
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    transaction = null;
                }
                _context.Entry(session).State = EntityState.Detached;
                var winner = await FindTodaySessionAsync(userId, date, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"DailySessionKey conflict without an existing session for {key}.");
                return (ClassifyExisting(winner, snapshotJson), winner);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return (GuidedSessionFromPlanResult.Created, session);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Loads a session by id for GET /sessions/{id} with owner/admin authz done
    /// by the caller. Returns null for unknown ids and non-guided workouts.
    /// </summary>
    public async Task<Workout?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => await _context.Workouts
            .IgnoreQueryFilters()
            .Include(workout => workout.Exercises.Where(exercise => !exercise.IsDeleted))
            .FirstOrDefaultAsync(workout => workout.Id == sessionId, cancellationToken);

    /// <summary>
    /// Today's session for the user, if any. The controller uses this to answer
    /// 409 session_completed BEFORE plan resolution: a completed day may also
    /// have an empty plan (weekly quota met), and per the closed spec the 409
    /// wins over 400 plan_empty there.
    /// </summary>
    public Task<Workout?> GetTodaySessionAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => FindTodaySessionAsync(userId, date, cancellationToken);

    private async Task<Workout?> FindTodaySessionAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEndExclusive = dayStart.AddDays(1);

        // Prefer the guided session if several rows share the day (legacy data).
        return await _context.Workouts
            .IgnoreQueryFilters()
            .Include(workout => workout.Exercises.Where(exercise => !exercise.IsDeleted))
            .Where(workout =>
                workout.UserId == userId
                && workout.Date >= dayStart
                && workout.Date < dayEndExclusive)
            .OrderByDescending(workout => workout.GuidedSnapshotJson != null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private GuidedSessionFromPlanResult ClassifyExisting(Workout existing, string snapshotJson)
    {
        if (existing.CompletedAt is not null)
        {
            return GuidedSessionFromPlanResult.AlreadyCompleted;
        }

        if (string.IsNullOrEmpty(existing.GuidedSnapshotJson))
        {
            // Adoption: a plain session started today (manual quick-log) becomes
            // the guided one; its snapshot is frozen here, exactly once.
            existing.GuidedSnapshotJson = snapshotJson;
            _context.SaveChanges();
        }

        return GuidedSessionFromPlanResult.Resumed;
    }
}

public enum GuidedSessionFromPlanResult
{
    Created,
    Resumed,
    AlreadyCompleted,
}
