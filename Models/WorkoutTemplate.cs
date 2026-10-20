public class WorkoutTemplate
{
    public int Id { get; set; }
    public string Type { get; set; }
    public int DaysOfWeek { get; set; }
    /// <summary>
    /// Ordered exercise slots. Replaces the implicit ExerciseTypeWorkoutTemplate
    /// join table (no order, no sets/reps/load) — see docs/routine-prescriptions-design.md.
    /// </summary>
    public List<WorkoutTemplateExercise> Exercises { get; set; } = [];
    public List<Routine> Routines { get; set; }
    public string Goal { get; set; }
}