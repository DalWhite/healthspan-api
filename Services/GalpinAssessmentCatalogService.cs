public class GalpinAssessmentCatalogService
{
    public List<GalpinAdaptationCatalogItem> GetCatalog()
    {
        return new List<GalpinAdaptationCatalogItem>
        {
            new() { Adaptation = GalpinAdaptationType.Skill, DisplayName = "Skill", Description = "Technical proficiency, coordination, and movement quality." },
            new() { Adaptation = GalpinAdaptationType.Speed, DisplayName = "Speed", Description = "Ability to move rapidly with minimal resistance." },
            new() { Adaptation = GalpinAdaptationType.Power, DisplayName = "Power", Description = "Ability to express force quickly." },
            new() { Adaptation = GalpinAdaptationType.Strength, DisplayName = "Strength", Description = "Maximum force production." },
            new() { Adaptation = GalpinAdaptationType.Hypertrophy, DisplayName = "Hypertrophy", Description = "Lean tissue gain and muscular development." },
            new() { Adaptation = GalpinAdaptationType.MuscularEndurance, DisplayName = "Muscular Endurance", Description = "Repeated submaximal work over time." },
            new() { Adaptation = GalpinAdaptationType.AnaerobicCapacity, DisplayName = "Anaerobic Capacity", Description = "Tolerance and output under high-intensity glycolytic work." },
            new() { Adaptation = GalpinAdaptationType.MaximalAerobicPower, DisplayName = "Maximal Aerobic Power", Description = "High-end aerobic output and oxygen utilization." },
            new() { Adaptation = GalpinAdaptationType.LongDurationEndurance, DisplayName = "Long Duration Endurance", Description = "Sustained output over extended durations." }
        };
    }
}

public class GalpinAdaptationCatalogItem
{
    public GalpinAdaptationType Adaptation { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
}