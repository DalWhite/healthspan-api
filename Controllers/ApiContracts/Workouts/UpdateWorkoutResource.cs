using System.ComponentModel.DataAnnotations;

public class UpdateWorkoutResource : BaseWorkoutResource, IValidatableObject
{
    public Guid Id { get; set; }
    [StringLength(100)]
    public string Type { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id == Guid.Empty)
        {
            yield return new ValidationResult(
                "A workout ID is required.",
                [nameof(Id)]);
        }

        if (Date == default)
        {
            yield return new ValidationResult(
                "A workout date is required.",
                [nameof(Date)]);
        }
    }
}
