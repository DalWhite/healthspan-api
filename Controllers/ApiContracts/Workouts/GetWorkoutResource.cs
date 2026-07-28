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
    public List<WorkoutHistoryExerciseResource> Exercises { get; set; } = [];
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
}
