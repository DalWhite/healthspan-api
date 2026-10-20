/// <summary>
/// One exercise slot inside a routine workout: what to do, in order, with the
/// planned targets. All target fields are optional — a slot with only the
/// exercise type is valid.
/// </summary>
public class ExercisePrescriptionResource
{
    public int Id { get; set; }
    public int ExerciseTypeId { get; set; }
    public string? ExerciseTypeName { get; set; }
    public int Order { get; set; }
    public int? Sets { get; set; }
    public int? Reps { get; set; }
    public double? LoadKg { get; set; }
    public int? RestSeconds { get; set; }
    public string? Notes { get; set; }
}
