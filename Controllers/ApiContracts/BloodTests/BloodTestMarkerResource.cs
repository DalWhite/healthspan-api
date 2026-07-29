#nullable enable

public class BloodTestMarkerResource
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public string? RawValueText { get; set; }
    public double? Confidence { get; set; }
    public MarkerStatus Status { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
    public bool ReferenceRangeFromReport { get; set; }
}
