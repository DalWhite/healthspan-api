using System.ComponentModel.DataAnnotations;

public class AddWorkoutSessionExerciseResource : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int TypeId { get; set; }

    // Sets/reps are optional: an exercise can be logged without numbers and
    // completed later. Range only applies when the user actually enters a value.
    [Range(1, 100)]
    public int? Series { get; set; }

    [Range(0, 1000)]
    public int? Repetitions { get; set; }

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
