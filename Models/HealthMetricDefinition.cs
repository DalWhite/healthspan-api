#nullable enable

public class HealthMetricDefinition
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefaultUnit { get; set; } = string.Empty;
    public HealthMetricCategory Category { get; set; }
    public string? Description { get; set; }
    public List<ReferenceRange> ReferenceRanges { get; set; } = new();
}

public enum HealthMetricCategory
{
    BloodAnalytics,
    Performance,
    Recovery,
    BodyComposition,
    GeneralHealth
}