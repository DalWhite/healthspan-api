#nullable enable

public class GetGalpinAssessmentResource
{
    public Guid Id { get; set; }
    public DateTime AssessedOnUtc { get; set; }
    public string? Notes { get; set; }
    public List<GalpinAssessmentScoreResource> Scores { get; set; } = new();
}