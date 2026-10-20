/// <summary>
/// POST /Workouts/apply-template — copy a routine workout's exercise slots into
/// the day's session ("start today's workout"). Date is YYYY-MM-DD; defaults to
/// today when omitted.
/// </summary>
public class ApplyTemplateResource
{
    public Guid RoutineId { get; set; }
    public int TemplateId { get; set; }
    public string? Date { get; set; }
}
