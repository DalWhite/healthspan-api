#nullable enable

public class GetBloodTestResource
{
    public Guid Id { get; set; }
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public string? SourceFileName { get; set; }
    public BloodTestProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingNotes { get; set; }
    public bool RequiresReview { get; set; }
    public List<BloodTestMarkerResource> Markers { get; set; } = new();
}