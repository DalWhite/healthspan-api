#nullable enable

public sealed class BloodTestSummary
{
    public Guid Id { get; set; }
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public string? SourceFileName { get; set; }
    public BloodTestProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingNotes { get; set; }
    public int MarkerCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
