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

/// <summary>Training dimension an activity contributes to: fuerza / salud / movilidad.</summary>
public enum WellnessDimension
{
    Strength,
    Health,
    Mobility
}
