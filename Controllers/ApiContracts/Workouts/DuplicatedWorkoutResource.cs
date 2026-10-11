public class DuplicatedWorkoutResource
{
    public Guid SourceWorkoutId { get; set; }
    public GetWorkoutResource Workout { get; set; } = new();
}
