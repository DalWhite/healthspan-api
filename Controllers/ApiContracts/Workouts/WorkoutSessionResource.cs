public class WorkoutSessionResource
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string Type { get; set; }
    public List<WorkoutSessionExerciseResource> Exercises { get; set; } = [];
    public List<int> RecentExerciseTypeIds { get; set; } = [];
    public List<WorkoutBlockResource> Blocks { get; set; } = [];

    /// <summary>Universal training tracker fields (null when not filled in).</summary>
    public string? Goal { get; set; }
    public List<string> SecondaryGoals { get; set; } = [];
    public string? Modality { get; set; }
    public int? PerceivedFatigue { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WorkoutSessionExerciseResource
{
    public Guid Id { get; set; }
    public int TypeId { get; set; }
    public string Name { get; set; }
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public decimal? Weight { get; set; }
    public decimal? BodyWeight { get; set; }
    public decimal? AddedWeight { get; set; }
    public string Comments { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>Universal training tracker fields.</summary>
    public Guid? BlockId { get; set; }
    public int? Order { get; set; }
    public int? Attempts { get; set; }
    public int? CompletedAttempts { get; set; }
    public string? QualityIntent { get; set; }
    public string? LoadType { get; set; }
    public List<string> Adaptations { get; set; } = [];
}
