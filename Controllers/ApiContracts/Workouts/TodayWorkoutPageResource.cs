#nullable enable

/// <summary>
/// Aggregate payload for the whole Today screen (t_101e2b0a): today's sessions,
/// the recent-workouts context (streak chip + "Últimos entrenos"), the previous
/// load per exercise type ("Anterior" column / PR badge) and the user body
/// weight — four separate round-trips (/Workouts/today/sessions,
/// /workouts?page=1&amp;pageSize=14, /Workouts/{id} x2, /HealthProfile) become
/// one. Exercise types stay a separate call owned by the exercise catalog.
/// </summary>
public sealed class TodayWorkoutPageResource
{
    public List<WorkoutSessionResource> Sessions { get; set; } = [];
    public List<TodayRecentWorkoutResource> RecentWorkouts { get; set; } = [];

    /// <summary>Exercise type id -> load logged in the two most recent sessions.</summary>
    public Dictionary<int, WorkoutHistoryExerciseResource> PreviousLoads { get; set; } = [];

    public decimal? ProfileWeightKg { get; set; }
}

/// <summary>
/// "Últimos entrenos" row. Exercises are omitted here — they travel only in
/// <see cref="TodayWorkoutPageResource.PreviousLoads"/>, which is what the
/// Today screen reads from recent sessions (t_101e2b0a payload budget).
/// </summary>
public sealed class TodayRecentWorkoutResource
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public int ExerciseCount { get; set; }
    public string? Modality { get; set; }
    public string? BlockLabel { get; set; }

    /// <summary>Heaviest logged exercise — feeds the row metric and doubles as
    /// an "Anterior" baseline. Null only for fully empty rows.</summary>
    public WorkoutHistoryExerciseResource? TopExercise { get; set; }

    public int Attempts { get; set; }
    public int CompletedAttempts { get; set; }
}
