#nullable enable

/// <summary>
/// One registered body-composition observation (t_358e09e9 K2).
/// User.Weight stays the overwritten scalar "current value"; this table is the
/// time series the monthly weight KPI needs. The metric definition is shared
/// (category BodyComposition) so future composition metrics reuse the table.
/// Raw readings are NEVER returned by the results endpoints (privacy §5):
/// only monthly aggregates surface.
/// </summary>
public class BodyCompositionReading
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int HealthMetricDefinitionId { get; set; }
    public decimal Value { get; set; }
    /// <summary>manual | csv | sync (spec §2 K2).</summary>
    public string Source { get; set; } = BodyCompositionReadingSources.Manual;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public User User { get; set; } = null!;
    public HealthMetricDefinition Metric { get; set; } = null!;
}

public static class BodyCompositionReadingSources
{
    public const string Manual = "manual";
    public const string Csv = "csv";
    public const string Sync = "sync";
}
