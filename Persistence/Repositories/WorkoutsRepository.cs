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
            .Include(candidate => candidate.Blocks)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId,
                cancellationToken);
        if (workout is null)
        {
            return false;
        }

        _context.Exercises.RemoveRange(workout.Exercises);
        _context.WorkoutBlocks.RemoveRange(workout.Blocks);
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

    /// <summary>
    /// Bulk variant of GetOrCreateWorkoutSessionAsync for CSV import jobs
    /// (t_c1f8e6ac): one range query for every date of the job, one insert
    /// batch for the missing daily sessions, instead of a Serializable
    /// transaction per date. Returns the workout id per date.
    ///
    /// Concurrency (t_633ebd61): two imports of the same user can compute the
    /// same DailySessionKeys at the same time. The loser's insert fails on
    /// IX_Workouts_DailySessionKey; the winner commits its sessions before its
    /// batch loop starts, so a single retry resolves every date with a plain
    /// range SELECT and an empty insert batch. The locked, serializable
    /// per-date path is kept as a last resort only: running it per date while
    /// the winner's batch transaction is still open deadlocks on gap locks
    /// (QA probe t_79d39b1e: 3/3 rounds).
    /// </summary>
    public async Task<Dictionary<DateOnly, Guid>> GetOrCreateWorkoutSessionsBulkAsync(
        string userId,
        IReadOnlyCollection<DateOnly> dates,
        string type,
        CancellationToken cancellationToken = default)
    {
        if (dates.Count == 0)
        {
            return new Dictionary<DateOnly, Guid>();
        }

        try
        {
            return await TryGetOrCreateWorkoutSessionsBulkAsync(
                userId, dates, type, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
        }

        try
        {
            // The concurrent import won and has committed its sessions by
            // now: the range SELECT resolves every date and no insert races.
            return await TryGetOrCreateWorkoutSessionsBulkAsync(
                userId, dates, type, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
        }

        // Last resort: the locked, serializable per-date path.
        var fallbackResult = new Dictionary<DateOnly, Guid>();
        foreach (var date in dates.Distinct().OrderBy(date => date))
        {
            var (workout, _) = await GetOrCreateWorkoutSessionAsync(
                userId,
                date,
                type,
                cancellationToken);
            fallbackResult[date] = workout.Id;
        }
        return fallbackResult;
    }

    private async Task<Dictionary<DateOnly, Guid>> TryGetOrCreateWorkoutSessionsBulkAsync(
        string userId,
        IReadOnlyCollection<DateOnly> dates,
        string type,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<DateOnly, Guid>();
        if (dates.Count == 0)
        {
            return result;
        }

        var sorted = dates.Distinct().OrderBy(date => date).ToList();
        var start = sorted[0].ToDateTime(TimeOnly.MinValue);
        var endExclusive = sorted[^1].ToDateTime(TimeOnly.MinValue).AddDays(1);

        var existingRows = await _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout =>
                workout.UserId == userId
                && workout.Date != null
                && workout.Date >= start
                && workout.Date < endExclusive)
            .Select(workout => new
            {
                workout.Id,
                workout.Date,
                workout.DailySessionKey,
                HasExercises = workout.Exercises.Any()
            })
            .ToListAsync(cancellationToken);

        var byDate = existingRows
            .GroupBy(row => DateOnly.FromDateTime(row.Date!.Value))
            .ToDictionary(group => group.Key, group => group.ToList());

        var newWorkouts = new List<Workout>();

        IDbContextTransaction transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(
                    cancellationToken);
            }

            foreach (var date in sorted)
            {
                var dailyKey = WorkoutDailySessionKey.Create(userId, date);

                if (!byDate.TryGetValue(date, out var candidates) || candidates.Count == 0)
                {
                    var workout = new Workout
                    {
                        Id = Guid.CreateVersion7(),
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        Type = string.IsNullOrWhiteSpace(type) ? "Training" : type.Trim(),
                        UserId = userId,
                        DailySessionKey = dailyKey,
                        Exercises = []
                    };
                    newWorkouts.Add(workout);
                    result[date] = workout.Id;
                    continue;
                }

                var picked = candidates
                    .OrderByDescending(row => row.HasExercises)
                    .ThenBy(row => row.Date)
                    .ThenBy(row => row.Id)
                    .First();

                if (picked.DailySessionKey == dailyKey)
                {
                    result[date] = picked.Id;
                    continue;
                }

                // The daily key lives on another workout (e.g. a
                // session-scoped one created by the tracker): move it to the
                // picked workout, mirroring GetOrCreateWorkoutSessionAsync.
                var tracked = await _context.Workouts
                    .IgnoreQueryFilters()
                    .FirstAsync(workout => workout.Id == picked.Id, cancellationToken);
                var conflicting = await _context.Workouts
                    .IgnoreQueryFilters()
                    .Where(workout =>
                        workout.Id != picked.Id
                        && workout.DailySessionKey == dailyKey)
                    .ToListAsync(cancellationToken);
                foreach (var conflict in conflicting)
                {
                    conflict.DailySessionKey = null;
                }
                tracked.DailySessionKey = dailyKey;
                result[date] = tracked.Id;
            }

            if (newWorkouts.Count > 0)
            {
                if (_context.Database.IsRelational())
                {
                    // Multi-row INSERT instead of EF change tracking: an
                    // import creates one session per distinct date (2.5k+ for
                    // a historical CSV) and EF pays ~2 ms/row client-side.
                    // Columns/defaults match what EF would write for a new
                    // Workout (SecondaryGoals serializes to "[]").
                    var values = new List<string>(newWorkouts.Count);
                    var parameters = new List<MySqlConnector.MySqlParameter>(newWorkouts.Count * 6);
                    for (var i = 0; i < newWorkouts.Count; i++)
                    {
                        var w = newWorkouts[i];
                        values.Add($"(@w{i}_id,@w{i}_date,@w{i}_type,@w{i}_user,@w{i}_key,@w{i}_goals)");
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_id", w.Id));
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_date", w.Date!.Value));
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_type", w.Type));
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_user", w.UserId));
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_key", w.DailySessionKey));
                        parameters.Add(new MySqlConnector.MySqlParameter($"@w{i}_goals", "[]"));
                    }

                    var sql =
                        "INSERT INTO Workouts (Id, Date, Type, UserId, DailySessionKey, SecondaryGoals) VALUES "
                        + string.Join(",", values);
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
                    }
                    catch (MySqlConnector.MySqlException ex)
                    {
                        // A concurrent import computed the same DailySessionKey
                        // and raced us on IX_Workouts_DailySessionKey
                        // (t_633ebd61). ExecuteSqlRawAsync throws MySqlException
                        // directly, so surface it as DbUpdateException to
                        // activate the serialized fallback below — same pattern
                        // as CsvImportPipeline.InsertBatchAsync.
                        throw new DbUpdateException(
                            "Bulk daily-session insert raced a concurrent import", ex);
                    }
                }
                else
                {
                    _context.Workouts.AddRange(newWorkouts);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                // Detach the failed transaction before the caller retries:
                // the retry starts a new transaction on this context, and EF
                // refuses BeginTransaction while a (rolled-back) one is still
                // attached.
                await transaction.DisposeAsync();
                transaction = null;
            }
            _context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
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

    /// <summary>
    /// Completed workouts = explicit CompletedAt (POST /workouts/{id}/complete),
    /// NOT "has exercises": an applied routine draft must not count as done.
    /// </summary>
    public Task<List<Workout>> GetCompletedWorkoutsSinceAsync(
        string userId,
        DateTime since,
        CancellationToken cancellationToken = default)
        => _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout =>
                workout.UserId == userId
                && workout.Date.HasValue
                && workout.Date.Value >= since
                && workout.CompletedAt != null)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// "Start today's workout": copies the routine's ordered exercise slots into
    /// the day's session as normal, editable exercises. Idempotent: an exercise
    /// type already present in the session is skipped. Never auto-completes.
    /// </summary>
    public async Task<ApplyTemplateOutcome> ApplyTemplateToSessionAsync(
        string userId,
        Guid routineId,
        int templateId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var template = await _context.WorkoutTemplate
            .IgnoreQueryFilters()
            .Include(workoutTemplate => workoutTemplate.Exercises)
                .ThenInclude(templateExercise => templateExercise.ExerciseType)
            .Include(workoutTemplate => workoutTemplate.Routines)
            .FirstOrDefaultAsync(
                workoutTemplate =>
                    workoutTemplate.Id == templateId
                    && workoutTemplate.Routines.Any(
                        routine => routine.Id == routineId && routine.UserId == userId),
                cancellationToken);
        if (template is null)
        {
            return new ApplyTemplateOutcome(ApplyTemplateResult.TemplateNotFound, null, false);
        }

        var slots = (template.Exercises ?? [])
            .OrderBy(templateExercise => templateExercise.Order)
            .ToList();
        if (slots.Count == 0)
        {
            return new ApplyTemplateOutcome(ApplyTemplateResult.TemplateEmpty, null, false);
        }

        var (session, created) = await GetOrCreateWorkoutSessionAsync(
            userId,
            date,
            template.Type,
            cancellationToken);

        var sessionTypeIds = (session.Exercises ?? [])
            .Where(exercise => exercise.TypeId.HasValue)
            .Select(exercise => exercise.TypeId!.Value)
            .ToHashSet();

        var added = 0;
        foreach (var slot in slots)
        {
            if (!sessionTypeIds.Add(slot.ExerciseTypeId))
            {
                continue;
            }

            var exercise = new Exercise
            {
                Id = Guid.NewGuid(),
                WorkoutId = session.Id,
                TypeId = slot.ExerciseTypeId,
                Series = slot.Sets,
                Repetitions = slot.Reps,
                Weight = slot.LoadKg,
                Comments = slot.Notes,
                SourceTemplateExerciseId = slot.Id,
                Tags = []
            };
            _context.Exercises.Add(exercise);
            added++;
        }

        if (added > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new ApplyTemplateOutcome(
            added > 0 ? ApplyTemplateResult.Applied : ApplyTemplateResult.AlreadyApplied,
            session,
            created);
    }

    /// <summary>
    /// Explicit workout completion. Idempotent: re-completing keeps the first
    /// CompletedAt.
    /// </summary>
    public async Task<Workout?> CompleteWorkoutAsync(
        Guid workoutId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var workout = await _context.Workouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId && candidate.UserId == userId,
                cancellationToken);
        if (workout is null)
        {
            return null;
        }

        workout.CompletedAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return await GetWorkoutSessionAsync(workoutId, userId, cancellationToken);
    }

    private IQueryable<Workout> SessionQuery(string userId, bool asNoTracking = true)
    {
        var query = _context.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.UserId == userId)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Adaptations)
                .ThenInclude(adaptation => adaptation.Adaptation)
            .Include(workout => workout.Blocks);

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
                        .ThenInclude(assignment => assignment.Muscle)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Adaptations)
                .ThenInclude(adaptation => adaptation.Adaptation)
            .Include(workout => workout.Blocks);

        return asNoTracking ? query.AsNoTracking() : query;
    }
}

public sealed record WorkoutHistoryPage(
    IReadOnlyList<Workout> Workouts,
    int TotalCount);

public enum ApplyTemplateResult
{
    Applied,
    AlreadyApplied,
    TemplateNotFound,
    TemplateEmpty
}

public sealed record ApplyTemplateOutcome(
    ApplyTemplateResult Result,
    Workout Workout,
    bool SessionCreated);
