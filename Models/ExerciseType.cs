using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

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
    public MuscleGroup MuscleGroup { get; set; }
    public int DifficultyLevel { get; set; }
    public List<User> FavoritedUsers { get; set; }
    public List<WorkoutTemplate> WorkoutTemplates { get; set; }
    public string WeightType { get; set; }
    
    /// <summary>
    /// Tags stored as JSON (equipment, mechanic, level, force, type)
    /// Serialized as a JSON string in the database
    /// </summary>
    [Column(TypeName = "longtext")]
    public List<ExerciseTag> Tags { get; set; } = new();
}