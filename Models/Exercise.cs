public class Exercise
{
    public Guid Id { get; set; }
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public double? Weight { get; set; }
    public double? MaxRepetition {get;set;}
    public int? Volume {get;set;}
    public int? Intensity { get; set; }
    public int? TypeId { get; set; }
    public ExerciseType Type { get; set; }
    public Guid? WorkoutId { get; set; }
    public Workout Workout { get; set; }
}