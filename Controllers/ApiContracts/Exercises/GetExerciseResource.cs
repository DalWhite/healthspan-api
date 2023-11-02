public class GetExerciseResource : BaseExerciseResource
{
    public Guid Id { get; set; }
    public double? MaxRepetition {get;set;}
    public int? Volume {get;set;}
    public int? Intensity { get; set; }
    public GetExerciseTypeResource Type { get; set; }
    public GetWorkoutResource Workout { get; set; }
}