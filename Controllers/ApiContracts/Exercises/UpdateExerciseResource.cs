public class UpdateExerciseResource : BaseExerciseResource
{
    public Guid Id { get; set; }
    public GetWorkoutResource Workout { get; set; }
}