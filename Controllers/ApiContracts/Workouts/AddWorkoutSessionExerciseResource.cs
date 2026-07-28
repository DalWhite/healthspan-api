using System.ComponentModel.DataAnnotations;

public class AddWorkoutSessionExerciseResource : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int TypeId { get; set; }

    [Range(1, 100)]
    public int Series { get; set; } = 1;

    [Range(0, 1000)]
    public int Repetitions { get; set; } = 8;

    [Range(0, 10000)]
    public decimal Weight { get; set; }

    [Range(0, 10000)]
    public decimal? BodyWeight { get; set; }

    [Range(0, 10000)]
    public decimal? AddedWeight { get; set; }

    public string Comments { get; set; }
    public List<string> Tags { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => ExerciseTagNormalizer.Validate(Tags);
}
