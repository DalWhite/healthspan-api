#nullable enable

public class BloodTestMarkerResult
{
    public Guid Id { get; set; }
    public Guid BloodTestId { get; set; }
    public BloodTest BloodTest { get; set; } = null!;
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? RawMarkerName { get; set; }
    public string? Unit { get; set; }
    public string? NormalizedUnit { get; set; }
    public string? RawValueText { get; set; }
    public decimal? Value { get; set; }
    public decimal? NormalizedValue { get; set; }
    public double? Confidence { get; set; }
    public MarkerStatus Status { get; set; } = MarkerStatus.RequiresReview;
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}

public enum MarkerStatus
{
    Unknown,
    InRange,
    Low,
    High,
    RequiresReview
}