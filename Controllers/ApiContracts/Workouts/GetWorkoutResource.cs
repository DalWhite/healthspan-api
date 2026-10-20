#nullable enable

public sealed class GetWorkoutResource
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int ExerciseCount { get; set; }
    public double TotalVolume { get; set; }
    public List<string> LeadingMuscles { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<WorkoutHistoryExerciseResource> Exercises { get; set; } = [];
    public List<WorkoutBlockHistoryResource> Blocks { get; set; } = [];

    /// <summary>Universal training tracker fields (null when not filled in).</summary>
    public string? Goal { get; set; }
    public List<string> SecondaryGoals { get; set; } = [];
    public string? Modality { get; set; }
    public int? PerceivedFatigue { get; set; }
}

public class WorkoutHistoryExerciseResource
{
    public Guid Id { get; set; }
    public int TypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public double? Weight { get; set; }
    public double? BodyWeight { get; set; }
    public double? AddedWeight { get; set; }
    public double Volume { get; set; }
    public string? Comments { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Muscles { get; set; } = [];

    /// <summary>Universal training tracker fields.</summary>
    public Guid? BlockId { get; set; }
    public int? Order { get; set; }
    public int? Attempts { get; set; }
    public int? CompletedAttempts { get; set; }
    public string? QualityIntent { get; set; }
    public string? LoadType { get; set; }
    /// <summary>Adaptations this logged exercise trained (override or type mapping).</summary>
    public List<string> Adaptations { get; set; } = [];
}

public sealed class WorkoutBlockHistoryResource
{
    public Guid Id { get; set; }
    public Guid? ClientEntityId { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = "Normal";
    public string? Label { get; set; }
    public int? Rounds { get; set; }
    public List<WorkoutHistoryExerciseResource> Exercises { get; set; } = [];
}
