public class UpdateExerciseTypeResource : BaseExerciseTypeResource
{
    public int Id { get; set; }
    public int? ParentExerciseTypeId { get; set; }
    public bool IsFavourite {get;set;}
}
