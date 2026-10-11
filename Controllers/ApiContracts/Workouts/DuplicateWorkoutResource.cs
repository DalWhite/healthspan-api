using System.ComponentModel.DataAnnotations;

public class DuplicateWorkoutResource : IValidatableObject
{
    public DateOnly Date { get; set; }

    [StringLength(100)]
    public string? Type { get; set; }

    /// <summary>
    /// When set, only these exercise ids (from the source workout) are copied.
    /// When null/empty, the whole day is duplicated.
    /// </summary>
    public List<Guid>? ExerciseIds { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date == default)
        {
            yield return new ValidationResult(
                "Date is required.",
                [nameof(Date)]);
        }
    }
}
