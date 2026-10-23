#nullable enable

using System.Text.Json.Serialization;

/// <summary>
/// Inbound payload for POST and PUT /api/activities. Unknown top-level fields
/// are rejected with 400 instead of being silently dropped (t_0e23bbe6):
/// per-type dynamic parameters must travel inside <see cref="Metadata"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class WellnessActivityWriteResource
{
    public string Type { get; set; } = string.Empty;
    public WellnessDimension? Dimension { get; set; }
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

/// <summary>
/// Paged envelope for GET /api/activities (spec §6, t_0e23bbe6):
/// {items,total,page,pageSize,hasMore}.
/// </summary>
public class PagedActivitiesResource
{
    public List<WellnessActivityResource> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}
