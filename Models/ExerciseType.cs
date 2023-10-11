using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ExerciseType
{
    public int Id { get; set; }
    public string Name {get;set;}
    public string Description {get;set;}
    public string MuscleGroup {get;set;}
}