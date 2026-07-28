public class WorkoutSessionResource
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string Type { get; set; }
    public List<WorkoutSessionExerciseResource> Exercises { get; set; } = [];
    public List<int> RecentExerciseTypeIds { get; set; } = [];
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
}
