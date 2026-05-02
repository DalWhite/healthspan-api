#nullable enable

public class AddGalpinAssessmentResource
{
    public DateTime AssessedOnUtc { get; set; }
    public string? Notes { get; set; }
    public List<GalpinAssessmentScoreResource> Scores { get; set; } = new();
}