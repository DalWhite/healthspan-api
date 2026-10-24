#nullable enable

/// <summary>
/// A logged wellness/bienestar activity (sauna, cold exposure, WimHof breathing,
/// meditation, or any custom type). Common parameters are first-class columns;
/// type-specific extras live in the JSON <see cref="Metadata"/> bag.
/// </summary>
public class WellnessActivity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;

    /// <summary>Normalized slug: sauna, cold_exposure, wim_hof, meditation, ... (extensible).</summary>
    public string Type { get; set; } = string.Empty;

    public WellnessDimension Dimension { get; set; }

    public DateTime OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? FrequencyPerWeek { get; set; }
    public decimal? Intensity { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
    public string? Notes { get; set; }

    /// <summary>Free-form per-type parameters, stored as JSON (JSONB on MySQL).</summary>
    public Dictionary<string, object?>? Metadata { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Training dimension an activity contributes to. JSON contract (t_0e23bbe6):
/// serialized as the lowercase strings "strength" | "health" | "mobility" in
/// both request bodies and responses; the FE maps its labels fuerza/salud/
/// movilidad to these canonical names. Integer values are still accepted for
/// compatibility; anything else is a 400, never a silent default.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(WellnessDimensionJsonConverter))]
public enum WellnessDimension
{
    Strength,
    Health,
    Mobility
}

/// <summary>
/// Writes <see cref="WellnessDimension"/> as the lowercase canonical strings
/// and reads them case-insensitively (legacy integers are also accepted).
/// </summary>
public sealed class WellnessDimensionJsonConverter
    : System.Text.Json.Serialization.JsonConverter<WellnessDimension>
{
    public override WellnessDimension Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
        {
            if (!reader.TryGetInt32(out var raw)
                || !Enum.IsDefined(typeof(WellnessDimension), raw))
            {
                throw new System.Text.Json.JsonException(
                    "Unsupported dimension value. Expected strength, health or mobility.");
            }
            return (WellnessDimension)raw;
        }

        var text = reader.GetString();
        switch (text?.Trim().ToLowerInvariant())
        {
            case "strength": return WellnessDimension.Strength;
            case "health": return WellnessDimension.Health;
            case "mobility": return WellnessDimension.Mobility;
            default:
                throw new System.Text.Json.JsonException(
                    $"Unsupported dimension value '{text}'. Expected strength, health or mobility.");
        }
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        WellnessDimension value,
        System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            WellnessDimension.Strength => "strength",
            WellnessDimension.Health => "health",
            WellnessDimension.Mobility => "mobility",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
}
