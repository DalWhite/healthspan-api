using Microsoft.EntityFrameworkCore;

public class RoutinesRepository(MainDbContext context)
{
    private readonly MainDbContext _context = context;

    public async Task<List<Routine>> GetRoutinesAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => await QueryWithRelations(userId)
            .OrderBy(routine => routine.Name)
            .ToListAsync(cancellationToken);

    public async Task<Routine?> GetRoutineAsync(
        Guid routineId,
        string userId,
        CancellationToken cancellationToken = default)
        => await QueryWithRelations(userId)
            .FirstOrDefaultAsync(routine => routine.Id == routineId, cancellationToken);

    public async Task<bool> NameExistsAsync(
        string userId,
        string name,
        Guid? excludeRoutineId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim().ToLower();
        return await _context.Routines
            .IgnoreQueryFilters()
            .AnyAsync(routine =>
                routine.UserId == userId
                && routine.Name.ToLower() == normalizedName
                && (excludeRoutineId == null || routine.Id != excludeRoutineId.Value),
                cancellationToken);
    }

    public async Task<Routine> AddRoutineAsync(
        Routine routine,
        CancellationToken cancellationToken = default)
    {
        routine.Version = 1;
        _context.Routines.Add(routine);
        await _context.SaveChangesAsync(cancellationToken);
        return routine;
    }

    /// <summary>
    /// Full structural replacement: the routine's templates (and their ordered
    /// exercises) are replaced wholesale and Version is bumped.
    /// Templates shared with other routines are detached, never deleted.
    /// </summary>
    public async Task<RoutineUpdateResult> UpdateRoutineAsync(
        Guid routineId,
        string userId,
        Routine replacement,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.Routines
            .IgnoreQueryFilters()
            .Include(routine => routine.WorkoutTemplates)
            .FirstOrDefaultAsync(
                routine => routine.Id == routineId && routine.UserId == userId,
                cancellationToken);
        if (existing is null)
        {
            return RoutineUpdateResult.NotFound;
        }

        var oldTemplates = (existing.WorkoutTemplates ?? []).ToList();
        existing.WorkoutTemplates = [];
        await _context.SaveChangesAsync(cancellationToken);

        await DeleteOrphanTemplatesAsync(oldTemplates, cancellationToken);

        existing.Name = replacement.Name;
        existing.Description = replacement.Description;
        existing.WorkoutTemplates = replacement.WorkoutTemplates ?? [];
        existing.Version += 1;

        await _context.SaveChangesAsync(cancellationToken);
        return RoutineUpdateResult.Updated;
    }

    public async Task<bool> DeleteRoutineAsync(
        Guid routineId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var routine = await _context.Routines
            .IgnoreQueryFilters()
            .Include(routine => routine.WorkoutTemplates)
            .FirstOrDefaultAsync(
                routine => routine.Id == routineId && routine.UserId == userId,
                cancellationToken);
        if (routine is null)
        {
            return false;
        }

        var templateIds = (routine.WorkoutTemplates ?? [])
            .Select(template => template.Id)
            .ToList();

        _context.Routines.Remove(routine);
        await _context.SaveChangesAsync(cancellationToken);

        if (templateIds.Count > 0)
        {
            var templates = await _context.WorkoutTemplate
                .IgnoreQueryFilters()
                .Where(template => templateIds.Contains(template.Id))
                .ToListAsync(cancellationToken);
            await DeleteOrphanTemplatesAsync(templates, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// "Save as routine" from History: a one-template routine built from a real
    /// logged workout, exercises in logged order, initial targets = the values
    /// actually done that day.
    /// </summary>
    public async Task<Routine?> CreateRoutineFromWorkoutAsync(
        Guid workoutId,
        string userId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var workout = await _context.Workouts
            .IgnoreQueryFilters()
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId && candidate.UserId == userId,
                cancellationToken);
        if (workout is null)
        {
            return null;
        }

        var exercises = workout.Exercises ?? [];
        var template = new WorkoutTemplate
        {
            Type = string.IsNullOrWhiteSpace(workout.Type) ? "Training" : workout.Type,
            DaysOfWeek = 1,
            Exercises = exercises
                .Where(exercise => exercise.TypeId is not null && exercise.TypeId > 0)
                .OrderBy(exercise => exercise.Id)
                .Select((exercise, index) => new WorkoutTemplateExercise
                {
                    ExerciseTypeId = exercise.TypeId!.Value,
                    Order = index + 1,
                    Sets = exercise.Series,
                    Reps = exercise.Repetitions,
                    LoadKg = exercise.Weight,
                    Notes = string.IsNullOrWhiteSpace(exercise.Comments) ? null : exercise.Comments
                })
                .ToList()
        };

        var routine = new Routine
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"{template.Type} · {workout.Date?.ToString("yyyy-MM-dd") ?? "Saved workout"}"
                : name.Trim(),
            UserId = userId,
            Version = 1,
            WorkoutTemplates = [template]
        };

        _context.Routines.Add(routine);
        await _context.SaveChangesAsync(cancellationToken);
        return routine;
    }

    /// <summary>
    /// Removes template rows that no longer belong to any routine. Their ordered
    /// exercises cascade; logged Exercises keep their history because
    /// SourceTemplateExerciseId is SetNull.
    /// </summary>
    private async Task DeleteOrphanTemplatesAsync(
        List<WorkoutTemplate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var candidateIds = candidates.Select(template => template.Id).ToList();
        var stillReferenced = await _context.WorkoutTemplate
            .IgnoreQueryFilters()
            .Where(template =>
                candidateIds.Contains(template.Id)
                && template.Routines.Any())
            .Select(template => template.Id)
            .ToListAsync(cancellationToken);

        var orphans = candidates
            .Where(template => !stillReferenced.Contains(template.Id))
            .ToList();
        if (orphans.Count > 0)
        {
            _context.WorkoutTemplate.RemoveRange(orphans);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private IQueryable<Routine> QueryWithRelations(string userId)
        => _context.Routines
            .IgnoreQueryFilters()
            .Where(routine => routine.UserId == userId)
            .Include(routine => routine.WorkoutTemplates)
                .ThenInclude(template => template.Exercises)
                    .ThenInclude(templateExercise => templateExercise.ExerciseType)
            .Include(routine => routine.WorkoutTemplates)
                .ThenInclude(template => template.Exercises)
                    .ThenInclude(templateExercise => templateExercise.ExerciseType.MuscleAssignments)
                        .ThenInclude(assignment => assignment.Muscle)
            .AsSplitQuery();
}

public enum RoutineUpdateResult
{
    Updated,
    NotFound
}
