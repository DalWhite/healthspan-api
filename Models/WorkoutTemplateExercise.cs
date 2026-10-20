/// <summary>
/// One ordered exercise slot inside a WorkoutTemplate (internal term: "prescription").
/// Never exposed to the UI with this name — the API contract calls it an exercise
/// with sets/reps/load inside a routine workout.
/// </summary>
public class WorkoutTemplateExercise
{
    public int Id { get; set; }
    public int WorkoutTemplateId { get; set; }
    public WorkoutTemplate WorkoutTemplate { get; set; }
    public int ExerciseTypeId { get; set; }
    public ExerciseType ExerciseType { get; set; }
    /// <summary>
    /// Dense position inside the template: 1..n, unique per template.
    /// </summary>
    public int Order { get; set; }
    public int? Sets { get; set; }
    public int? Reps { get; set; }
    public double? LoadKg { get; set; }
    public int? RestSeconds { get; set; }
    /// <summary>
    /// Coach explanation for this exercise slot (replaces the abandoned
    /// WorkoutTemplate.ExerciseInstructions JSON attempt).
    /// </summary>
    public string? Notes { get; set; }
}
