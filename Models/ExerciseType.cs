public class ExerciseType
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageRoute { get; set; }
    public int? PrimaryMuscleId { get; set; }
    public Muscle PrimaryMuscle { get; set; }
    public int? SecondaryMuscleId { get; set; }
    public Muscle SecondaryMuscle { get; set; }
}