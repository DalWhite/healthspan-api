/// <summary>
/// POST /Workouts/{id}/save-as-routine — "save as routine" from a logged
/// workout. Name is optional; when omitted the API suggests one from the
/// workout type and date.
/// </summary>
public class SaveAsRoutineResource
{
    public string Name { get; set; }
}
