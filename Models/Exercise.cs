public class Exercise
{
    public Guid Id { get; set; }
    public int? Series {get;set;}
    public int? Repetitions {get;set;}
    public int? Weight {get;set;}
    public ExerciseType Type { get; set; }
    public Workout Workout {get;set;}
}