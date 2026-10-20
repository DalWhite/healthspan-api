public class Exercise
{
    public Guid Id { get; set; }
    public Guid? ClientEntityId { get; set; }
    public int? Series { get; set; }
    public int? Repetitions { get; set; }
    public double? Weight { get; set; }
    public double? BodyWeight { get; set; }
    public double? AddedWeight { get; set; }
    public double? MaxRepetition { get; set; }
    public int? Volume { get; set; }
    public int? Intensity { get; set; }
    public int? TypeId { get; set; }
    public string Comments { get; set; }
    public List<string> Tags { get; set; } = [];
    public ExerciseType Type { get; set; }
    public Guid? WorkoutId { get; set; }
    public Workout Workout { get; set; }
    /// <summary>
    /// Lineage to the routine exercise slot this was applied from
    /// (POST /workouts/apply-template). Survives template deletion (SetNull),
    /// so adherence can compare planned vs executed.
    /// </summary>
    public int? SourceTemplateExerciseId { get; set; }
    /// <summary>
    /// Block this exercise belongs to. Null means the implicit Normal block, so
    /// sessions logged before the universal tracker are unchanged.
    /// </summary>
    public Guid? BlockId { get; set; }
    public WorkoutBlock Block { get; set; }
    /// <summary>Dense 1..n position inside the block.</summary>
    public int? Order { get; set; }
    /// <summary>Attempts of a skill/speed/power move (e.g. muscle-ups 8/0).</summary>
    public int? Attempts { get; set; }
    public int? CompletedAttempts { get; set; }
    /// <summary>Free-text intention that can override the type adaptation mapping.</summary>
    public string? QualityIntent { get; set; }
    public ExerciseLoadType? LoadType { get; set; }
    public List<ExerciseAdaptation> Adaptations { get; set; } = [];
}
