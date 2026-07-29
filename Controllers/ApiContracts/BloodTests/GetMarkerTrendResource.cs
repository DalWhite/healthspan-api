#nullable enable

public class GetMarkerTrendResource
{
    public string MetricKey { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public bool HasIncompatibleUnits { get; set; }
    public List<string> AvailableUnits { get; set; } = new();
    public List<GetMarkerTrendPointResource> Points { get; set; } = new();
}

public class GetMarkerTrendPointResource
{
    public Guid BloodTestId { get; set; }
    public DateTime Time { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
}
