using System.ComponentModel.DataAnnotations;
using Controllers.ApiContracts.ExerciseTypes;

public class BaseExerciseTypeResource
{
    [StringLength(100, ErrorMessage = "Exercise name cannot exceed 100 characters.")]
    public string Name { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string Description { get; set; }

    [StringLength(500, ErrorMessage = "Image URL cannot exceed 500 characters.")]
    public string ImageRoute { get; set; }

    public string WeightType { get; set; }
    public List<ExerciseTagResource> Tags { get; set; } = new();
    public List<ExerciseTypeMuscleAssignmentResource> MuscleAssignments { get; set; } = new();
}
