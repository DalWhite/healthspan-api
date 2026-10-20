/// <summary>
/// Shared mapping between the Routine domain model and its API resource,
/// plus the weak-ETag helpers used for optimistic concurrency (If-Match).
/// </summary>
public static class RoutineMapper
{
    public static RoutineResource Map(Routine routine)
        => new()
        {
            Id = routine.Id,
            Name = routine.Name,
            Description = routine.Description,
            UserId = routine.UserId,
            TrainerId = routine.TrainerId,
            Version = routine.Version,
            WorkoutTemplates = (routine.WorkoutTemplates ?? [])
                .OrderBy(template => template.Id)
                .Select(template => new WorkoutTemplateResource
                {
                    Id = template.Id,
                    Type = template.Type,
                    FrequencyPerWeek = template.DaysOfWeek,
                    Exercises = (template.Exercises ?? [])
                        .OrderBy(exercise => exercise.Order)
                        .Select(RoutinePlanService.MapExercise)
                        .ToList()
                })
                .ToList()
        };

    public static string ETagOf(int version) => $"W/\"v{version}\"";

    public static int? ParseETagVersion(string ifMatch)
    {
        var value = ifMatch.Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        value = value.Trim('"');
        if (!value.StartsWith('v'))
        {
            return null;
        }

        return int.TryParse(value[1..], out var version) ? version : null;
    }
}
