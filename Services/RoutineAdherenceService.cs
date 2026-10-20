/// <summary>
/// Planned vs completed per routine workout day, for History progress.
/// FrequencyPerWeek is a weekly quota (Monday-Sunday): a day is "planned"
/// while the quota for its week is not met yet. Compliance is measured per
/// ordered exercise slot (sets/reps targets), not just exercise type.
/// "Adherence" is an internal term; it never appears in the UI.
/// </summary>
public class RoutineAdherenceService(
    RoutinesRepository routinesRepository,
    WorkoutsRepository workoutsRepository)
{
    public async Task<RoutineAdherenceResource?> GetAdherenceAsync(
        Guid routineId,
        string userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var routine = await routinesRepository.GetRoutineAsync(routineId, userId, cancellationToken);
        if (routine is null)
        {
            return null;
        }

        var templates = (routine.WorkoutTemplates ?? [])
            .OrderBy(t => t.Id)
            .ToList();

        var completedWorkouts = await workoutsRepository.GetCompletedWorkoutsSinceAsync(
            userId,
            from.ToDateTime(TimeOnly.MinValue),
            cancellationToken);
        var untilExclusive = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        completedWorkouts = completedWorkouts
            .Where(w => w.Date.HasValue && w.Date.Value < untilExclusive)
            .ToList();

        var days = new List<RoutineAdherenceDayResource>();
        var plannedWorkouts = 0;
        var completedWorkoutCount = 0;

        foreach (var template in templates)
        {
            var quota = Math.Max(1, template.DaysOfWeek);
            var slots = (template.Exercises ?? [])
                .OrderBy(e => e.Order)
                .ToList();

            var templateCompleted = completedWorkouts
                .Where(w => RoutinePlanService.MatchesTemplate(w, template))
                .ToList();
            completedWorkoutCount += templateCompleted.Count;

            var weekStart = RoutinePlanService.WeekStart(from);
            for (; weekStart <= to; weekStart = weekStart.AddDays(7))
            {
                plannedWorkouts += quota;

                var completedThisWeek = 0;
                for (var offset = 0; offset < 7; offset++)
                {
                    var date = weekStart.AddDays(offset);
                    if (date < from || date > to)
                    {
                        continue;
                    }

                    var dayWorkouts = templateCompleted
                        .Where(w => DateOnly.FromDateTime(w.Date!.Value) == date)
                        .ToList();

                    var planned = completedThisWeek < quota;
                    if (dayWorkouts.Count > 0)
                    {
                        completedThisWeek += dayWorkouts.Count;
                    }

                    var (executed, setsMet, repsMet) = MeasureSlots(slots, dayWorkouts);

                    days.Add(new RoutineAdherenceDayResource
                    {
                        Date = date.ToString("yyyy-MM-dd"),
                        TemplateId = template.Id,
                        Type = template.Type,
                        Planned = planned,
                        Completed = dayWorkouts.Count > 0,
                        PlannedExerciseCount = slots.Count,
                        CompletedExerciseCount = executed,
                        TargetSetsMetCount = setsMet,
                        TargetRepsMetCount = repsMet
                    });
                }
            }
        }

        days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));

        return new RoutineAdherenceResource
        {
            RoutineId = routine.Id,
            RoutineName = routine.Name,
            From = from.ToString("yyyy-MM-dd"),
            To = to.ToString("yyyy-MM-dd"),
            PlannedWorkouts = plannedWorkouts,
            CompletedWorkouts = completedWorkoutCount,
            Days = days
        };
    }

    /// <summary>
    /// Per-slot compliance for one day's completed workouts. A slot counts as
    /// executed when a logged exercise carries lineage to it
    /// (SourceTemplateExerciseId) or, for hand-logged workouts, matches its
    /// exercise type. Sets target is met when the executed sets add up to the
    /// prescribed number; reps target when any executed set reaches the
    /// prescribed reps.
    /// </summary>
    private static (int Executed, int SetsMet, int RepsMet) MeasureSlots(
        List<WorkoutTemplateExercise> slots,
        List<Workout> dayWorkouts)
    {
        var exercises = dayWorkouts
            .SelectMany(w => w.Exercises ?? [])
            .ToList();

        var executed = 0;
        var setsMet = 0;
        var repsMet = 0;

        foreach (var slot in slots)
        {
            var matches = exercises
                .Where(e => e.SourceTemplateExerciseId == slot.Id)
                .ToList();
            if (matches.Count == 0)
            {
                matches = exercises
                    .Where(e => e.SourceTemplateExerciseId is null
                        && e.TypeId == slot.ExerciseTypeId)
                    .ToList();
            }

            if (matches.Count == 0)
            {
                continue;
            }

            executed++;
            if (slot.Sets.HasValue
                && matches.Sum(e => e.Series ?? 1) >= slot.Sets.Value)
            {
                setsMet++;
            }

            if (slot.Reps.HasValue
                && matches.Max(e => e.Repetitions ?? 0) >= slot.Reps.Value)
            {
                repsMet++;
            }
        }

        return (executed, setsMet, repsMet);
    }
}
