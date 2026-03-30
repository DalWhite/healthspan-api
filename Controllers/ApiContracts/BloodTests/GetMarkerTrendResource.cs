#nullable enable

public class GetMarkerTrendResource
{
    public string MetricKey { get; set; } = string.Empty;
    public List<GetMarkerTrendPointResource> Points { get; set; } = new();
}

public class GetMarkerTrendPointResource
{
    public DateTime Time { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
}