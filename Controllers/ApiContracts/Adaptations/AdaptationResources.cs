#nullable enable

/// <summary>Catalog of the 9 Galpin adaptations in canonical order.</summary>
public sealed class AdaptationResource
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = string.Empty;
    public int TargetSessionsPerWeek { get; set; }
    public int ExerciseTypeCount { get; set; }
}

public sealed class AdaptationMappingResource
{
    public int ExerciseTypeId { get; set; }
    public List<string> Primary { get; set; } = [];
    public List<string> Secondary { get; set; } = [];
}

public sealed class AdaptationMappingInputResource
{
    public List<string> Primary { get; set; } = [];
    public List<string> Secondary { get; set; } = [];
}

public sealed class AdaptationCoverageResource
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int Sessions { get; set; }
    public List<AdaptationCoverageItemResource> Items { get; set; } = [];
}

public sealed class AdaptationCoverageItemResource
{
    public int AdaptationId { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public double Volume { get; set; }
    public int Sessions { get; set; }
    public int TargetSessionsPerWeek { get; set; }
    /// <summary>Sessions / target. 1 = covered, 0 = never trained.</summary>
    public double CoverageRatio { get; set; }
    public DateTime? LastSessionDate { get; set; }
    public int? DaysSinceLastSession { get; set; }
    /// <summary>up | flat | down | none, compared with the previous window.</summary>
    public string Trend { get; set; } = "none";
}

public sealed class ReadinessResource
{
    public DateTime Date { get; set; }
    /// <summary>0..10. 10 = rested and ready, 0 = exhausted.</summary>
    public double Score { get; set; }
    public double FatigueLoad7d { get; set; }
    public int Sessions7d { get; set; }
    public double TotalVolume7d { get; set; }
    public List<ReadinessAdaptationResource> PerAdaptation { get; set; } = [];
    public List<ReadinessSuggestionResource> Suggestions { get; set; } = [];

    /// <summary>
    /// Optional sponsored slot shown alongside the organic suggestions. Always
    /// carries the visible "Patrocinado" label and Sponsored=true; null when
    /// the feature is off or the user is not eligible. Never mixed into
    /// Suggestions.
    /// </summary>
    public SponsoredSlotResource? Sponsored { get; set; }
}

public sealed class ReadinessAdaptationResource
{
    public int AdaptationId { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public double Volume7d { get; set; }
    public int Sessions7d { get; set; }
    public DateTime? LastSessionDate { get; set; }
    public int? DaysSinceLastSession { get; set; }
    public string Trend { get; set; } = "none";
}

public sealed class ReadinessSuggestionResource
{
    public int AdaptationId { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
