/// <summary>
/// Unified routine payload for GET/POST/PUT. The FE sends the full structure on
/// PUT (full structural replacement) and echoes Version back via If-Match.
/// </summary>
public class RoutineResource
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string UserId { get; set; }
    /// <summary>
    /// Coach who created/assigned this routine (null for self-created routines).
    /// Derived server-side from the authenticated requester, never sent by the FE.
    /// </summary>
    public string? TrainerId { get; set; }
    public int Version { get; set; }
    public List<WorkoutTemplateResource> WorkoutTemplates { get; set; } = [];
}
