#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes a UTC <see cref="DateTimeOffset"/> with the 'Z' designator
/// (System.Text.Json default is "+00:00"). Read path only (t_dbb640bd).
/// </summary>
public sealed class UtcZDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
}

/// <summary>List-projection variant: the same UTC instant, date-only.</summary>
public sealed class UtcDateOnlyOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd"));
}

/// <summary>
/// Inbound payload for POST and PUT /api/activities. Unknown top-level fields
/// are rejected with 400 instead of being silently dropped (t_0e23bbe6):
/// per-type dynamic parameters must travel inside <see cref="Metadata"/>.
/// PUT semantics (QA-C F2, t_dbb640bd): PUT is a FULL replacement — omitted
/// fields are cleared (including <see cref="Metadata"/>), never merged.
/// Clients editing an activity must resend the complete resource.
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

/// <summary>
/// Outbound resource for GET /api/activities (t_dbb640bd, QA-C F1):
/// timestamps are <see cref="DateTimeOffset"/> so GET responses always carry
/// the UTC designator (MySQL/EF return Kind=Unspecified, which System.Text.Json
/// would serialize without 'Z' and clients could read as local time);
/// the decimal columns are exposed as double so MySQL scale (e.g.
/// 7.0000000000000000000000000000) is not leaked to clients.
/// </summary>
public class WellnessActivityResource
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public WellnessDimension Dimension { get; set; }
    [JsonConverter(typeof(UtcZDateTimeOffsetConverter))]
    public DateTimeOffset OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public double? FrequencyPerWeek { get; set; }
    public double? Intensity { get; set; }
    public double? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    [JsonConverter(typeof(UtcZDateTimeOffsetConverter))]
    public DateTimeOffset CreatedAtUtc { get; set; }
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

/// <summary>
/// List projection for GET /api/activities?fields=minimal (t_101e2b0a): only
/// the fields the history list renders (title, dimension badge, param badges,
/// duration, day grouping) — id, notes, metadata and createdAtUtc are dropped
/// and the client fetches the full resource on demand (detail sheet, edit).
/// 100 rows must stay <=15KB; the full shape costs ~29KB.
/// </summary>
public class WellnessActivitySummaryResource
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public WellnessDimension Dimension { get; set; }
    // The list groups rows by day and never renders an hour; shipping the
    // 12-char date (instead of the 28-char timestamp) is what keeps 100 rows
    // inside the 15KB budget. Detail/edit refetch the full resource.
    [JsonConverter(typeof(UtcDateOnlyOffsetConverter))]
    public DateTimeOffset OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public double? FrequencyPerWeek { get; set; }
    public double? Intensity { get; set; }
    public double? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
}

/// <summary>Paged envelope carrying <see cref="WellnessActivitySummaryResource"/> items.</summary>
public class PagedActivitiesSummaryResource
{
    public List<WellnessActivitySummaryResource> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}
