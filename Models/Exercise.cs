using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Exercise
{
    public Guid Id { get; set; }
    public ExerciseType? Type { get; set; }
    public int? Series {get;set;}
    public int? Repetitions {get;set;}
    public int? Weight {get;set;}
    public int? WorkoutId {get;set;}
    public Workout Workout {get;set;}
}