public class Muscle
{
    public int Id { get; set; }
    public string Name { get; set; }
    public MuscleSide Side { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
}
public enum MuscleSide
{
    front,
    back
}
public enum MuscleGroup
{
    Chest,
    Back,
    Arms,
    Core,
    Legs,
    Shoulders,
    Glutes
}
