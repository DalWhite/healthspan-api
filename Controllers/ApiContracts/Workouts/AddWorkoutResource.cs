#nullable enable

using System.ComponentModel.DataAnnotations;

public sealed class AddWorkoutResource : IValidatableObject
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "Training";
    public string? UserId { get; set; }

    /// <summary>Session goal: one of the 9 Galpin adaptations.</summary>
    public string? Goal { get; set; }
    public List<string> SecondaryGoals { get; set; } = [];
    public string? Modality { get; set; }

    [Range(1, 5)]
    public int? PerceivedFatigue { get; set; }

    /// <summary>Optional session structure. Omitted = unstructured session.</summary>
    public List<WorkoutBlockInputResource> Blocks { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date == default)
        {
            yield return new ValidationResult(
                "A workout date is required.",
                [nameof(Date)]);
        }

        if (!string.IsNullOrWhiteSpace(Goal) && !TrainingTrackerParser.TryAdaptation(Goal, out _))
        {
            yield return new ValidationResult($"Unknown goal '{Goal}'.", [nameof(Goal)]);
        }

        foreach (var secondaryGoal in SecondaryGoals ?? [])
        {
            if (!TrainingTrackerParser.TryAdaptation(secondaryGoal, out _))
            {
                yield return new ValidationResult($"Unknown goal '{secondaryGoal}'.", [nameof(SecondaryGoals)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(Modality) && !TrainingTrackerParser.TryModality(Modality, out _))
        {
            yield return new ValidationResult($"Unknown modality '{Modality}'.", [nameof(Modality)]);
        }

        foreach (var block in Blocks ?? [])
        {
            if (block is null)
            {
                continue;
            }

            foreach (var error in block.Validate(validationContext))
            {
                yield return error;
            }
        }
    }
}
