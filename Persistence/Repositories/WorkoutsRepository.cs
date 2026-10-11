using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services;

public class WorkoutsRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionLocks = new();
    private readonly MainDbContext _context;
    public WorkoutsRepository(MainDbContext context)
    {
        _context = context;
    }
    public async Task<WorkoutHistoryPage> GetWorkoutHistoryAsync(
        string userId,
        DateOnly? from,
        DateOnly? to,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default,
        string search = "",
        string tag = "")
    {
        var query = WorkoutHistoryQuery(userId);

        if (from.HasValue)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(workout => workout.Date >= fromDate);
        }

        if (to.HasValue)
        {
            var toExclusive = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(workout => workout.Date < toExclusive);
        }

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(workout =>
                workout.Type.Contains(normalizedSearch)
                || workout.Exercises.Any(exercise =>
                    exercise.Type.Name.Contains(normalizedSearch)));
        }

        var normalizedTag = tag?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedTag))
        {
            var candidates = await query
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var filtered = candidates
                .Where(workout => workout.Exercises.Any(exercise =>
                    ExerciseTagNormalizer.Normalize(exercise.Tags)
                        .Contains(
                            normalizedTag,
                            StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(workout => workout.Date)
                .ThenByDescending(workout => workout.Id)
                .ToList();
            var filteredCount = filtered.Count;

            if (page.HasValue && pageSize.HasValue)
            {
                filtered = filtered
                    .Skip((page.Value - 1) * pageSize.Value)
                    .Take(pageSize.Value)
                    .ToList();
            }

            return new WorkoutHistoryPage(
                filtered,
                filteredCount);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        query = query
            .OrderByDescending(workout => workout.Date)
            .ThenByDescending(workout => workout.Id);

        if (page.HasValue && pageSize.HasValue)
        {
            query = query
                .Skip((page.Value - 1) * pageSize.Value)
                .Take(pageSize.Value);
        }

        var workouts = await query
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new WorkoutHistoryPage(workouts, totalCount);
    }

    public Task<Workout> GetWorkoutHistoryDetailsAsync(
        Guid workoutId,
        string userId,
        CancellationToken cancellationToken = default)
        => WorkoutHistoryQuery(userId)
            .FirstOrDefaultAsync(workout => workout.Id == workoutId, cancellationToken);

    public async Task<Workout> UpdateWorkoutHistoryAsync(
        Guid workoutId,
        string userId,
        DateTime date,
        string type,
        CancellationToken cancellationToken = default)
    {
        var workout = await WorkoutHistoryQuery(userId, asNoTracking: false)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId,
                cancellationToken);
        if (workout is null)
        {
            return null;
        }

        workout.Date = date;
        if (!string.IsNullOrWhiteSpace(type))
        {
            workout.Type = type.Trim();
        }

        workout.DailySessionKey = WorkoutDailySessionKey.CreateForSession(
            workout.UserId,
            DateOnly.FromDateTime(date),
            workout.Id);

        await _context.SaveChangesAsync(cancellationToken);
        return workout;
    }

    public async Task<bool> DeleteWorkoutHistoryAsync(
        Guid workoutId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var workout = await _context.Workouts
            .IgnoreQueryFilters()
            .Where(candidate => candidate.UserId == userId)
            .Include(candidate => candidate.Exercises)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId,
                cancellationToken);
        if (workout is null)
        {
            return false;
        }

        _context.Exercises.RemoveRange(workout.Exercises);
        _context.Workouts.Remove(workout);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<Workout> GetWorkoutSessionAsync(
        Guid workoutId,
        string userId,
        CancellationToken cancellationToken = default)
        => SessionQuery(userId)
            .FirstOrDefaultAsync(workout => workout.Id == workoutId, cancellationToken);

    public Task<Workout> GetWorkoutSessionForDateAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return SessionQuery(userId)
            .OrderByDescending(workout => workout.Exercises.Any())
            .ThenBy(workout => workout.Date)
            .ThenBy(workout => workout.Id)
            .FirstOrDefaultAsync(
                workout => workout.Date >= start && workout.Date < end,
                cancellationToken);
    }

    public Task<List<Workout>> GetWorkoutSessionsForDateAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return SessionQuery(userId)
            .Where(workout =>
                workout.Date >= start
                && workout.Date < end)
            .OrderBy(workout => workout.Date)
            .ThenBy(workout => workout.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Workout> CreateWorkoutSessionAsync(
        string userId,
        DateOnly date,
        string type,
        CancellationToken cancellationToken = default)
    {
        var workoutId = Guid.NewGuid();
        var workout = new Workout
        {
            Id = workoutId,
            Date = date.ToDateTime(
                date == DateOnly.FromDateTime(DateTime.Today)
                    ? TimeOnly.FromDateTime(DateTime.Now)
                    : TimeOnly.MinValue),
            Type = string.IsNullOrWhiteSpace(type)
                ? "Training"
                : type.Trim(),
            UserId = userId,
            DailySessionKey =
                WorkoutDailySessionKey.CreateForSession(
                    userId,
                    date,
                    workoutId),
            Exercises = []
        };

        _context.Workouts.Add(workout);
        await _context.SaveChangesAsync(cancellationToken);
        return workout;
    }

    public async Task<(Workout Workout, bool Created)> GetOrCreateWorkoutSessionAsync(
        string userId,
        DateOnly date,
        string type,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"{userId}:{date:yyyy-MM-dd}";
        var dailySessionKey = WorkoutDailySessionKey.Create(userId, date);
        var sessionLock = SessionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);

        IDbContextTransaction transaction = null;

        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var start = date.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            var existing = await SessionQuery(userId, asNoTracking: false)
                .OrderByDescending(workout => workout.Exercises.Any())
                .ThenBy(workout => workout.Date)
                .ThenBy(workout => workout.Id)
                .FirstOrDefaultAsync(
                    workout => workout.Date >= start && workout.Date < end,
                    cancellationToken);

            if (existing is not null)
            {
                if (existing.DailySessionKey != dailySessionKey)
                {
                    var conflictingWorkout = await _context.Workouts
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(
                            workout =>
                                workout.Id != existing.Id
                                && workout.DailySessionKey == dailySessionKey,
                            cancellationToken);
                    if (conflictingWorkout is not null)
                    {
                        conflictingWorkout.DailySessionKey = null;
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    existing.DailySessionKey = dailySessionKey;
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return (existing, false);
            }

            var workout = new Workout
            {
                Id = Guid.NewGuid(),
                Date = date.ToDateTime(TimeOnly.MinValue),
                Type = string.IsNullOrWhiteSpace(type) ? "Training" : type.Trim(),
                UserId = userId,
                DailySessionKey = dailySessionKey,
                Exercises = []
            };

            _context.Workouts.Add(workout);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (workout, true);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                transaction = null;
            }

            _context.ChangeTracker.Clear();

            var start = date.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            var existing = await SessionQuery(userId)
                .OrderByDescending(workout => workout.Exercises.Any())
                .ThenBy(workout => workout.Date)
                .ThenBy(workout => workout.Id)
                .FirstOrDefaultAsync(
                    workout => workout.Date >= start && workout.Date < end,
                    cancellationToken);

            if (existing is not null)
            {
                return (existing, false);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            sessionLock.Release();
        }
    }

    public async Task<List<int>> GetRecentExerciseTypeIdsAsync(
        string userId,
        Guid? excludedWorkoutId,
        int count = 6,
        CancellationToken cancellationToken = default)
    {
        var recentTypeIds = await _context.Exercises
            .IgnoreQueryFilters()
            .Where(exercise =>
                exercise.Workout.UserId == userId
                && exercise.TypeId.HasValue
                && (!excludedWorkoutId.HasValue || exercise.WorkoutId != excludedWorkoutId))
            .OrderByDescending(exercise => exercise.Workout.Date)
            .ThenByDescending(exercise => exercise.Id)
            .Select(exercise => exercise.TypeId.Value)
            .Take(50)
            .ToListAsync(cancellationToken);

        return recentTypeIds
            .Distinct()
            .Take(count)
            .ToList();
    }

    public Task<List<Workout>> GetWeeklyTrainingWorkoutsAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout =>
                workout.UserId == userId
                && workout.Date.HasValue)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
                    .ThenInclude(type => type.MuscleAssignments)
                        .ThenInclude(assignment => assignment.Muscle)
            .OrderByDescending(workout => workout.Date)
            .ThenByDescending(workout => workout.Id)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<Workout?> DuplicateWorkoutSessionAsync(
        Guid sourceWorkoutId,
        string userId,
        DateOnly targetDate,
        string? type,
        IReadOnlyCollection<Guid>? exerciseIds,
        CancellationToken cancellationToken = default)
    {
        var source = await SessionQuery(userId)
            .FirstOrDefaultAsync(
                workout => workout.Id == sourceWorkoutId,
                cancellationToken);
        if (source is null)
        {
            return null;
        }

        var selectedExercises = (source.Exercises ?? [])
            .Where(exercise =>
                exerciseIds is null
                || exerciseIds.Count == 0
                || exerciseIds.Contains(exercise.Id))
            .ToList();

        var targetWorkoutId = Guid.NewGuid();
        var target = new Workout
        {
            Id = targetWorkoutId,
            Date = targetDate.ToDateTime(TimeOnly.MinValue),
            Type = string.IsNullOrWhiteSpace(type) ? source.Type : type.Trim(),
            UserId = userId,
            DailySessionKey = WorkoutDailySessionKey.CreateForSession(
                userId,
                targetDate,
                targetWorkoutId),
            Exercises = []
        };

        foreach (var sourceExercise in selectedExercises)
        {
            target.Exercises.Add(new Exercise
            {
                Id = Guid.NewGuid(),
                WorkoutId = targetWorkoutId,
                TypeId = sourceExercise.TypeId,
                Series = sourceExercise.Series,
                Repetitions = sourceExercise.Repetitions,
                Weight = sourceExercise.Weight,
                BodyWeight = sourceExercise.BodyWeight,
                AddedWeight = sourceExercise.AddedWeight,
                MaxRepetition = sourceExercise.MaxRepetition,
                Comments = sourceExercise.Comments,
                Tags = ExerciseTagNormalizer.Normalize(sourceExercise.Tags)
            });
        }

        _context.Workouts.Add(target);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The target date already owns the daily session key. If that
            // workout is empty it is a placeholder and the duplicate replaces
            // it; otherwise the duplicate simply lives as an extra session.
            _context.ChangeTracker.Clear();

            var conflicting = await _context.Workouts
                .IgnoreQueryFilters()
                .Where(workout => workout.UserId == userId)
                .Include(workout => workout.Exercises)
                .FirstOrDefaultAsync(
                    workout =>
                        workout.Date >= targetDate.ToDateTime(TimeOnly.MinValue)
                        && workout.Date < targetDate.ToDateTime(TimeOnly.MinValue).AddDays(1),
                    cancellationToken);

            if (conflicting is not null && conflicting.Exercises.Count == 0)
            {
                _context.Workouts.Remove(conflicting);
                await _context.SaveChangesAsync(cancellationToken);
                _context.ChangeTracker.Clear();
            }

            target.DailySessionKey = WorkoutDailySessionKey.CreateForSession(
                userId,
                targetDate,
                targetWorkoutId);
            _context.Workouts.Add(target);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await GetWorkoutHistoryDetailsAsync(
            target.Id,
            userId,
            cancellationToken);
    }

    private IQueryable<Workout> SessionQuery(string userId, bool asNoTracking = true)
    {
        var query = _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.UserId == userId)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    private IQueryable<Workout> WorkoutHistoryQuery(
        string userId,
        bool asNoTracking = true)
    {
        var query = _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.UserId == userId)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
                    .ThenInclude(type => type.MuscleAssignments)
                        .ThenInclude(assignment => assignment.Muscle);

        return asNoTracking ? query.AsNoTracking() : query;
    }
}

public sealed record WorkoutHistoryPage(
    IReadOnlyList<Workout> Workouts,
    int TotalCount);
