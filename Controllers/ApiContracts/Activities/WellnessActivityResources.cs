#nullable enable

/// <summary>Inbound payload for POST and PUT /api/activities.</summary>
public class WellnessActivityWriteResource
{
    public string Type { get; set; } = string.Empty;
    public WellnessDimension Dimension { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? FrequencyPerWeek { get; set; }
    public decimal? Intensity { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>Outbound resource for GET /api/activities.</summary>
public class WellnessActivityResource
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public WellnessDimension Dimension { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? FrequencyPerWeek { get; set; }
    public decimal? Intensity { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
