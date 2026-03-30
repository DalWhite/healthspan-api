#nullable enable

public class GetGalpinAdaptationTrendResource
{
    public GalpinAdaptationType Adaptation { get; set; }
    public List<GetGalpinAdaptationTrendPointResource> Points { get; set; } = new();
}

public class GetGalpinAdaptationTrendPointResource
{
    public DateTime Time { get; set; }
    public decimal Score { get; set; }
}