using System.Text.Json.Serialization;

/// <summary>
/// GET /Routines/plan?date=YYYY-MM-DD — the routine workouts scheduled for a
/// day, in routine order. Internal name is "plan"; the FE surface is Today.
/// </summary>
public class RoutinePlanResource
{
    public Guid RoutineId { get; set; }
    public string RoutineName { get; set; }
    public int TemplateId { get; set; }
    public string Type { get; set; }
    public int FrequencyPerWeek { get; set; }
    public List<ExercisePrescriptionResource> Exercises { get; set; } = [];

    [JsonIgnore]
    public int ExerciseCount => Exercises.Count;
}
