#nullable enable

public class BloodTest
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public string? SourceFileName { get; set; }
    public string? SourceContentType { get; set; }
    public string? SourceBlobContainer { get; set; }
    public string? SourceBlobName { get; set; }
    public string? SourceBlobUri { get; set; }
    public BloodTestProcessingStatus ProcessingStatus { get; set; } = BloodTestProcessingStatus.PendingReview;
    public string? ProcessingNotes { get; set; }
    public string? RawExtractionPayload { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<BloodTestMarkerResult> MarkerResults { get; set; } = new();
}

public enum BloodTestProcessingStatus
{
    PendingReview,
    Processed,
    ExtractionFailed,
    Processing
}