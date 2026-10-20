/// <summary>
/// GET /Workouts/routine-adherence — planned vs completed per routine workout
/// day. "Adherence" is an internal term; the FE surface is History progress.
/// </summary>
public class RoutineAdherenceResource
{
    public Guid RoutineId { get; set; }
    public string RoutineName { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public int PlannedWorkouts { get; set; }
    public int CompletedWorkouts { get; set; }
    public List<RoutineAdherenceDayResource> Days { get; set; } = [];
}

public class RoutineAdherenceDayResource
{
    public string Date { get; set; }
    public int TemplateId { get; set; }
    public string Type { get; set; }
    /// <summary>The template was still due that day (weekly quota not met yet).</summary>
    public bool Planned { get; set; }
    public bool Completed { get; set; }
    public int PlannedExerciseCount { get; set; }
    public int CompletedExerciseCount { get; set; }
    public int TargetSetsMetCount { get; set; }
    public int TargetRepsMetCount { get; set; }
}
