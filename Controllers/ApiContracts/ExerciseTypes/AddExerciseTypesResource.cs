public class AddExerciseTypeResource: BaseExerciseTypeResource
{
    /// <summary>
    /// Parent exercise ID if this is a variation
    /// Leave null for main exercises
    /// </summary>
    public int? ParentExerciseTypeId { get; set; }
}
