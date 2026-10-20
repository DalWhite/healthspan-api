#nullable enable

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Input for a block inside a session (supersets, circuits, EMOM, ...).
/// Order is dense 1..n inside the session.
/// </summary>
public sealed class WorkoutBlockInputResource : IValidatableObject
{
    /// <summary>Offline-first id; replaying the same payload never duplicates the block.</summary>
    public Guid? ClientEntityId { get; set; }
    public int Order { get; set; } = 1;
    /// <summary>Normal | Superset | Circuito | EMOM | AMRAP | Tabata | Ronda</summary>
    public string? Type { get; set; }
    [StringLength(100)]
    public string? Label { get; set; }
    [Range(1, 100)]
    public int? Rounds { get; set; }
    public List<SessionExerciseInputResource> Exercises { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Order < 1)
        {
            yield return new ValidationResult("Block order must be 1 or greater.", [nameof(Order)]);
        }

        if (!string.IsNullOrWhiteSpace(Type) && !TrainingTrackerParser.TryBlockType(Type, out _))
        {
            yield return new ValidationResult(
                $"Unknown block type '{Type}'.",
                [nameof(Type)]);
        }

        foreach (var exercise in Exercises)
        {
            if (exercise is null)
            {
                continue;
            }

            foreach (var error in exercise.Validate(validationContext))
            {
                yield return error;
            }
        }
    }
}

/// <summary>
/// A logged exercise. Sets/reps are optional on purpose: the universal tracker
/// accepts sessions without numbers (a skill round, a run, a mobility flow).
/// </summary>
public sealed class SessionExerciseInputResource : IValidatableObject
{
    public Guid? ClientEntityId { get; set; }

    [Range(1, int.MaxValue)]
    public int TypeId { get; set; }

    public int? Order { get; set; }

    [Range(1, 100)]
    public int? Series { get; set; }

    [Range(0, 1000)]
    public int? Repetitions { get; set; }

    [Range(0, 10000)]
    public decimal? Weight { get; set; }

    [Range(0, 10000)]
    public decimal? BodyWeight { get; set; }

    [Range(0, 10000)]
    public decimal? AddedWeight { get; set; }

    [Range(0, 1000)]
    public int? Attempts { get; set; }

    [Range(0, 1000)]
    public int? CompletedAttempts { get; set; }

    [StringLength(200)]
    public string? QualityIntent { get; set; }

    /// <summary>External | Bodyweight | AddedWeight | Machine</summary>
    public string? LoadType { get; set; }

    public string? Comments { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Per-session adaptation override. Without it the canonical exercise-type
    /// mapping is used.
    /// </summary>
    public List<string> Adaptations { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TypeId < 1)
        {
            yield return new ValidationResult("An exercise type id is required.", [nameof(TypeId)]);
        }

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

/// <summary>
/// Partial session update (PATCH /workouts/{id}). Null fields mean "keep as is".
/// </summary>
public sealed class PatchWorkoutSessionResource : IValidatableObject
{
    public string? Type { get; set; }
    public DateTime? Date { get; set; }
    public string? Goal { get; set; }
    public List<string>? SecondaryGoals { get; set; }
    public string? Modality { get; set; }

    [Range(1, 5)]
    public int? PerceivedFatigue { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date.HasValue && Date.Value == default)
        {
            yield return new ValidationResult("A workout date is required.", [nameof(Date)]);
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
    }
}

/// <summary>Response shape of a session block.</summary>
public sealed class WorkoutBlockResource
{
    public Guid Id { get; set; }
    public Guid? ClientEntityId { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = "Normal";
    public string? Label { get; set; }
    public int? Rounds { get; set; }
    public List<WorkoutSessionExerciseResource> Exercises { get; set; } = [];
}

/// <summary>
/// Central parser for the tracker wire enums. Kept as strings on the wire so the
/// contracts stay additive and independent from System.Text.Json enum options.
/// </summary>
public static class TrainingTrackerParser
{
    public static bool TryAdaptation(string? value, out GalpinAdaptationType adaptation)
        => TryParse(value, out adaptation);

    public static bool TryModality(string? value, out WorkoutModality modality)
        => TryParse(value, out modality);

    public static bool TryBlockType(string? value, out WorkoutBlockType blockType)
        => TryParse(value, out blockType);

    public static bool TryLoadType(string? value, out ExerciseLoadType loadType)
        => TryParse(value, out loadType);

    /// <summary>Accepts the canonical name or the 1..9 position of an adaptation.</summary>
    public static bool TryAdaptationPosition(string? value, out GalpinAdaptationType adaptation)
    {
        adaptation = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (int.TryParse(value.Trim(), out var position)
            && position >= 1
            && position <= 9)
        {
            adaptation = (GalpinAdaptationType)(position - 1);
            return true;
        }

        return TryAdaptation(value, out adaptation);
    }

    private static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out parsed);
    }
}
