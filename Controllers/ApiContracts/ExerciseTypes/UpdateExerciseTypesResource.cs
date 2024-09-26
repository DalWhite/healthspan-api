public class UpdateExerciseTypeResource : BaseExerciseTypeResource
{
    public int Id { get; set; }
    public int? PrimaryMuscleId {get;set;}
    public int? SecondaryMuscleId {get;set;}
    public bool IsFavourite {get;set;}
}