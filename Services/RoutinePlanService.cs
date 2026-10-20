using Microsoft.EntityFrameworkCore;

/// <summary>
/// Computes which routine workouts are due for a given day. Internal name
/// is "plan"; the FE surface is Today.
/// FrequencyPerWeek (stored as DaysOfWeek) is a weekly quota: a template is
/// due on any day until that many workouts have been completed for it in the
/// current week (Monday-Sunday). 0 or negative is treated as 1 (legacy rows).
/// </summary>
public class RoutinePlanService(
    RoutinesRepository routinesRepository,
    WorkoutsRepository workoutsRepository)
{
    public async Task<List<RoutinePlanResource>> GetPlanAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var routines = await routinesRepository.GetRoutinesAsync(userId, cancellationToken);
        var (weekCompleted, _) = await GetWeekCompletedAsync(
            userId,
            date,
            cancellationToken);

        var plans = new List<RoutinePlanResource>();

        foreach (var routine in routines)
        {
            foreach (var template in (routine.WorkoutTemplates ?? []).OrderBy(t => t.Id))
            {
                var quota = Math.Max(1, template.DaysOfWeek);
                var completedThisWeek = weekCompleted
                    .Where(w => MatchesTemplate(w, template))
                    .Count();
                if (completedThisWeek >= quota)
                {
                    continue;
                }

                plans.Add(new RoutinePlanResource
                {
                    RoutineId = routine.Id,
                    RoutineName = routine.Name,
                    TemplateId = template.Id,
                    Type = template.Type,
                    FrequencyPerWeek = quota,
                    Exercises = (template.Exercises ?? [])
                        .OrderBy(e => e.Order)
                        .Select(MapExercise)
                        .ToList()
                });
            }
        }

        return plans;
    }

    /// <summary>
    /// Completed workouts (CompletedAt != null) in the Monday-Sunday week
    /// containing the date. Shared by plan and adherence.
    /// </summary>
    public async Task<(List<Workout> Workouts, DateOnly WeekStart)> GetWeekCompletedAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var weekStart = WeekStart(date);
        var weekEndExclusive = weekStart.AddDays(7);
        var workouts = await workoutsRepository.GetCompletedWorkoutsSinceAsync(
            userId,
            weekStart.ToDateTime(TimeOnly.MinValue),
            cancellationToken);
        workouts = workouts
            .Where(w => w.Date.HasValue && w.Date.Value < weekEndExclusive.ToDateTime(TimeOnly.MinValue))
            .ToList();
        return (workouts, weekStart);
    }

    public static DateOnly WeekStart(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// A completed workout belongs to a template when one of its exercises
    /// carries lineage to the template's slots (SourceTemplateExerciseId), or,
    /// for workouts logged by hand, when the workout type matches the template
    /// type.
    /// </summary>
    public static bool MatchesTemplate(Workout workout, WorkoutTemplate template)
    {
        var slotIds = (template.Exercises ?? [])
            .Select(slot => slot.Id)
            .ToHashSet();
        if (slotIds.Count > 0 && (workout.Exercises ?? []).Any(
                exercise => exercise.SourceTemplateExerciseId is int id
                    && slotIds.Contains(id)))
        {
            return true;
        }

        return string.Equals(workout.Type, template.Type, StringComparison.OrdinalIgnoreCase);
    }

    public static ExercisePrescriptionResource MapExercise(WorkoutTemplateExercise exercise)
        => new()
        {
            Id = exercise.Id,
            ExerciseTypeId = exercise.ExerciseTypeId,
            ExerciseTypeName = exercise.ExerciseType?.Name,
            Order = exercise.Order,
            Sets = exercise.Sets,
            Reps = exercise.Reps,
            LoadKg = exercise.LoadKg,
            RestSeconds = exercise.RestSeconds,
            Notes = exercise.Notes
        };
}
