using System.ComponentModel.DataAnnotations;

public class BaseExerciseResource : IValidatableObject
{
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public decimal? Weight { get; set; }
    public string Comments { get; set; }
    public List<string> Tags { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => ExerciseTagNormalizer.Validate(Tags);
}
