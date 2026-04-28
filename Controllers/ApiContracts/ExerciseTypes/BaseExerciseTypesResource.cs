using Controllers.ApiContracts.ExerciseTypes;

public class BaseExerciseTypeResource
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageRoute { get; set; }
    public string WeightType { get; set; }
    public List<ExerciseTagResource> Tags { get; set; } = new();
    public List<ExerciseTypeMuscleAssignmentResource> MuscleAssignments { get; set; } = new();
}
