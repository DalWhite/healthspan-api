/// <summary>
/// One workout day inside a routine. FrequencyPerWeek is the design-contract
/// name for DaysOfWeek (1-7 sessions per week).
/// </summary>
public class WorkoutTemplateResource
{
    public int Id { get; set; }
    public string Type { get; set; }
    public int FrequencyPerWeek { get; set; }
    /// <summary>
    /// Ordered exercise slots. "Prescription" is an internal term and never
    /// appears in the API or UI.
    /// </summary>
    public List<ExercisePrescriptionResource> Exercises { get; set; } = [];
}
