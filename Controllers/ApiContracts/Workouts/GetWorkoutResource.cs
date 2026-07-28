public class GetWorkoutResource : BaseWorkoutResource
{
    public Guid Id { get; set; }
    public string Type { get; set; }
    public int ExerciseCount { get; set; }
    public double TotalVolume { get; set; }
    public List<string> LeadingMuscles { get; set; } = [];
    public List<WorkoutHistoryExerciseResource> Exercises { get; set; } = [];
}

public class WorkoutHistoryExerciseResource
{
    public Guid Id { get; set; }
    public int TypeId { get; set; }
    public string Name { get; set; }
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public double? Weight { get; set; }
    public double Volume { get; set; }
    public string Comments { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Muscles { get; set; } = [];
}
