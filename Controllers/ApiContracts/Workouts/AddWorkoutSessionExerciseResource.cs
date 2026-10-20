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

    /// <summary>Optional block id; unknown ids are rejected with 400.</summary>
    public Guid? BlockId { get; set; }
    public int? Order { get; set; }

    [Range(0, 1000)]
    public int? Attempts { get; set; }

    [Range(0, 1000)]
    public int? CompletedAttempts { get; set; }

    [StringLength(200)]
    public string? QualityIntent { get; set; }

    /// <summary>Bodyweight | External | Band | Kettlebell | Machine | Poles | Other</summary>
    public string? LoadType { get; set; }

    /// <summary>Per-session adaptation override for this logged exercise.</summary>
    public List<string> Adaptations { get; set; } = [];

    /// <summary>Offline-first id of the exercise created on the client.</summary>
    public Guid? ClientEntityId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(LoadType) && !TrainingTrackerParser.TryLoadType(LoadType, out _))
        {
            yield return new ValidationResult($"Unknown load type '{LoadType}'.", [nameof(LoadType)]);
        }

        foreach (var adaptation in Adaptations ?? [])
        {
            if (!TrainingTrackerParser.TryAdaptation(adaptation, out _))
            {
                yield return new ValidationResult($"Unknown adaptation '{adaptation}'.", [nameof(Adaptations)]);
            }
        }

        foreach (var error in ExerciseTagNormalizer.Validate(Tags))
        {
            yield return error;
        }
    }
}
