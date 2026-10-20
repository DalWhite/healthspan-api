/// <summary>
/// GET /routines/next — the workout the user should do next, resolved from the
/// weekly frequency of each routine workout day against what they have already
/// completed this week. Internal name is "next due"; the FE surface is
/// "Next workout".
/// </summary>
public class RoutineNextWorkoutResource
{
    /// <summary>
    /// Day the workout becomes actionable: today while the weekly quota is not
    /// met, otherwise the Monday that resets it.
    /// </summary>
    public DateOnly Date { get; set; }
    /// <summary>
    /// True when the workout is pending right now (Date == today), which is what
    /// makes the "Start today's workout" action available.
    /// </summary>
    public bool PendingNow { get; set; }
    public Guid RoutineId { get; set; }
    public string RoutineName { get; set; }
    public int TemplateId { get; set; }
    public string Type { get; set; }
    public int FrequencyPerWeek { get; set; }
    public List<ExercisePrescriptionResource> Exercises { get; set; } = [];
}
