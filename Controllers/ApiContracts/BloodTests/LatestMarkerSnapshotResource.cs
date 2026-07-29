#nullable enable

public class LatestMarkerSnapshotResource
{
    public Guid BloodTestId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
    public DateTime TestDate { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}
