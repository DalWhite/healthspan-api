public class WorkoutTemplate
{
    public int Id { get; set; }
    public string Type { get; set; }
    public int DaysOfWeek { get; set; }
    public List<ExerciseType> ExerciseTypes { get; set; }
    public List<Routine> Routines { get; set; }
    public string Goal { get; set; }
}