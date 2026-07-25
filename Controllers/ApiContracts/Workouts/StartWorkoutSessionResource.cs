using System.ComponentModel.DataAnnotations;

public class StartWorkoutSessionResource : IValidatableObject
{
    public DateOnly Date { get; set; }

    [StringLength(100)]
    public string Type { get; set; } = "Training";

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
