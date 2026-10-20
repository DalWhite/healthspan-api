using Microsoft.EntityFrameworkCore;

namespace healthspan_api.Persistence.Repositories;

/// <summary>
/// Writes the universal training tracker pieces (session meta, blocks,
/// exercises with attempts/adaptations) inside an existing session.
/// ClientEntityId makes every write idempotent so an offline replay never
/// duplicates a block or an exercise.
/// </summary>
public sealed class TrainingTrackerRepository(MainDbContext context)
{
    public async Task<Workout?> FindSessionAsync(
        Guid workoutId,
        string userId,
        CancellationToken cancellationToken = default)
        => await context.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.Id == workoutId && workout.UserId == userId)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Type)
            .Include(workout => workout.Exercises)
                .ThenInclude(exercise => exercise.Adaptations)
                .ThenInclude(adaptation => adaptation.Adaptation)
            .Include(workout => workout.Blocks)
            .FirstOrDefaultAsync(workout => workout.Id == workoutId, cancellationToken);

    public async Task<(WorkoutBlock Block, bool Created)> UpsertBlockAsync(
        Guid workoutId,
        string userId,
        WorkoutBlock block,
        CancellationToken cancellationToken = default)
    {
        if (block.ClientEntityId is not null)
        {
            var existing = await context.WorkoutBlocks
                .Where(candidate =>
                    candidate.WorkoutId == workoutId
                    && candidate.ClientEntityId == block.ClientEntityId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return (existing, created: false);
            }
        }

        block.WorkoutId = workoutId;

        if (block.Order <= 0)
        {
            block.Order = await NextBlockOrderAsync(workoutId, cancellationToken);
        }

        context.WorkoutBlocks.Add(block);
        await context.SaveChangesAsync(cancellationToken);

        return (block, created: true);
    }

    public async Task<(Exercise Exercise, bool Created)> UpsertExerciseAsync(
        Guid workoutId,
        string userId,
        Exercise exercise,
        IReadOnlyCollection<GalpinAdaptationType> adaptations,
        CancellationToken cancellationToken = default)
    {
        if (exercise.ClientEntityId is not null)
        {
            var existing = await context.Exercises
                .IgnoreQueryFilters()
                .Where(candidate =>
                    candidate.WorkoutId == workoutId
                    && candidate.ClientEntityId == exercise.ClientEntityId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return (existing, created: false);
            }
        }

        exercise.WorkoutId = workoutId;

        if (adaptations is not null && adaptations.Count > 0)
        {
            var adaptationIds = await context.Adaptations
                .Where(adaptation => adaptations.Contains(adaptation.Type))
                .Select(adaptation => new { adaptation.Id, adaptation.Order })
                .ToListAsync(cancellationToken);

            exercise.Adaptations = adaptationIds
                .OrderBy(adaptation => adaptation.Order)
                .Select(adaptation => new ExerciseAdaptation
                {
                    AdaptationId = adaptation.Id
                })
                .ToList();
        }

        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(cancellationToken);

        return (exercise, created: true);
    }

    /// <summary>
    /// PATCH semantics: a null argument keeps the current value.
    /// </summary>
    public async Task<Workout?> UpdateSessionMetaAsync(
        Guid workoutId,
        string userId,
        GalpinAdaptationType? goal,
        List<GalpinAdaptationType>? secondaryGoals,
        WorkoutModality? modality,
        int? perceivedFatigue,
        CancellationToken cancellationToken = default)
    {
        var workout = await context.Workouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == workoutId && candidate.UserId == userId,
                cancellationToken);
        if (workout is null)
        {
            return null;
        }

        if (goal.HasValue)
        {
            workout.Goal = goal;
        }

        if (secondaryGoals is not null)
        {
            workout.SecondaryGoals = secondaryGoals;
        }

        if (modality.HasValue)
        {
            workout.Modality = modality;
        }

        if (perceivedFatigue.HasValue)
        {
            workout.PerceivedFatigue = perceivedFatigue;
        }

        await context.SaveChangesAsync(cancellationToken);

        return await FindSessionAsync(workoutId, userId, cancellationToken);
    }

    private async Task<int> NextBlockOrderAsync(
        Guid workoutId,
        CancellationToken cancellationToken)
    {
        var orders = await context.WorkoutBlocks
            .Where(block => block.WorkoutId == workoutId)
            .Select(block => block.Order)
            .ToListAsync(cancellationToken);

        return orders.Count == 0 ? 1 : orders.Max() + 1;
    }
}
