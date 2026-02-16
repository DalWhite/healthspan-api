public class AddExerciseTypeResource: BaseExerciseTypeResource
{
    public int? PrimaryMuscleId {get;set;}
    public int? SecondaryMuscleId {get;set;}
    
    /// <summary>
    /// Parent exercise ID if this is a variation
    /// Leave null for main exercises
    /// </summary>
    public int? ParentExerciseTypeId { get; set; }
}