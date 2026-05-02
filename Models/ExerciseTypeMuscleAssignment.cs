public class ExerciseTypeMuscleAssignment
{
    public int Id { get; set; }
    public int ExerciseTypeId { get; set; }
    public ExerciseType ExerciseType { get; set; }
    public int MuscleId { get; set; }
    public Muscle Muscle { get; set; }
    public ExerciseMuscleRole Role { get; set; }
    public int SortOrder { get; set; }
}

public enum ExerciseMuscleRole
{
    Primary,
    Secondary
}
