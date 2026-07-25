using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services;

public class WorkoutsRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionLocks = new();
    private readonly MainDbContext _context;
    private readonly CurrentUserProvider _userProvider;
    public WorkoutsRepository(MainDbContext context, CurrentUserProvider userProvider)
    {
        _context = context;
        _userProvider = userProvider;
    }
    public Guid AddWorkout(Workout workout)
    {
        _context.Workouts.Add(workout);
        _context.SaveChanges();
        return workout.Id;
    }
    public Workout GetWorkout(Guid Id)
    {
        return _context.Workouts.Find(Id);
    }
    public List<Workout> GetWorkouts(string userId)
    {
        return _context.Workouts
        .IgnoreQueryFilters()
        .Where(w => w.UserId == userId)
        .ToList();
    }
    public void UpdateWorkout(Workout workout)
    {
        var existingEntity = GetWorkout(workout.Id);
        if (workout.Date is not null)
        {
            existingEntity.Date = workout.Date;
        }
        _context.SaveChanges();
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
            .FirstOrDefaultAsync(
                workout => workout.Date >= start && workout.Date < end,
                cancellationToken);
    }

    public async Task<(Workout Workout, bool Created)> GetOrCreateWorkoutSessionAsync(
        string userId,
        DateOnly date,
        string type,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"{userId}:{date:yyyy-MM-dd}";
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

            var existing = await GetWorkoutSessionForDateAsync(userId, date, cancellationToken);
            if (existing is not null)
            {
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

    private IQueryable<Workout> SessionQuery(string userId)
        => _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.UserId == userId)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
            .AsNoTracking();
}
