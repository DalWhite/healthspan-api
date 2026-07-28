#nullable enable

using System.ComponentModel.DataAnnotations;

public sealed class AddWorkoutResource : IValidatableObject
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "Training";
    public string? UserId { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (Date == default)
        {
            yield return new ValidationResult(
                "A workout date is required.",
                [nameof(Date)]);
        }
    }
}
