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
}
