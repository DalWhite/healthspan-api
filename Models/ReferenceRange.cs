#nullable enable

public class ReferenceRange
{
    public int Id { get; set; }
    public int HealthMetricDefinitionId { get; set; }
    public HealthMetricDefinition HealthMetricDefinition { get; set; } = null!;
    public BiologicalSex? BiologicalSex { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string Unit { get; set; } = string.Empty;
}