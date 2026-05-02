public class GetExerciseTypeResource : BaseExerciseTypeResource
{
    public int Id { get; set; }
    public bool IsFavourite { get; set; }
    
    /// <summary>
    /// Parent exercise ID if this is a variation, null if it's a main exercise
    /// </summary>
    public int? ParentExerciseTypeId { get; set; }
    
    /// <summary>
    /// List of variation exercises (only populated for main exercises)
    /// </summary>
    public List<GetExerciseTypeResource> Variations { get; set; } = new();
}
