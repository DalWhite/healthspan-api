public class GetExerciseTypeResource : BaseExerciseTypeResource
{
    public int Id { get; set; }
    public int? PrimaryMuscleId {get;set;}
    public int? SecondaryMuscleId {get;set;}
}