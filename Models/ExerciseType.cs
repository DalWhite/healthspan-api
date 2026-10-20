using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

public class ExerciseType
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageRoute { get; set; }
    public int DifficultyLevel { get; set; }
    public List<User> FavoritedUsers { get; set; }
    public string WeightType { get; set; }
    public List<ExerciseTypeMuscleAssignment> MuscleAssignments { get; set; } = new();
    
    /// <summary>
    /// Tags stored as JSON (equipment, mechanic, level, force, type)
    /// Serialized as a JSON string in the database
    /// </summary>
    [Column(TypeName = "longtext")]
    public List<ExerciseTag> Tags { get; set; } = new();
    
    /// <summary>
    /// Parent exercise type for variations
    /// Null for main exercises, contains the ID of the parent for variations
    /// Example: "Situps" is main, "Situps - Kettlebell" has ParentExerciseTypeId = SitupsId
    /// </summary>
    public int? ParentExerciseTypeId { get; set; }
    
    /// <summary>
    /// Navigation property to the parent exercise
    /// </summary>
    [ForeignKey(nameof(ParentExerciseTypeId))]
    public ExerciseType ParentExerciseType { get; set; }
    
    /// <summary>
    /// Navigation property for variations/child exercises
    /// </summary>
    public List<ExerciseType> Variations { get; set; } = new();
}
