public class Muscle
{
    public int Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public MuscleSide Side { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
    public int SortOrder { get; set; }
    public List<ExerciseTypeMuscleAssignment> ExerciseTypeAssignments { get; set; } = new();
}
public enum MuscleSide
{
    front,
    back,
    both
}
public enum MuscleGroup
{
    Chest,
    Shoulders,
    Back,
    Arms,
    Core,
    Legs,
    Glutes
}
