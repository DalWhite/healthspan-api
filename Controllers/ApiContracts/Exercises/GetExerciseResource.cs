public class GetExerciseResource : BaseExerciseResource
{
    public Guid Id { get; set; }
    public GetExerciseTypeResource Type { get; set; }
    public GetWorkoutResource Workout { get; set; }
}