#nullable enable

using System.Text.Json.Serialization;

/// <summary>
/// The 9 Andy Galpin adaptations, stored in canonical order. The order is not
/// cosmetic: near adaptations can be trained in parallel, far ones interfere
/// (Chart of Interference), and readiness suggestions depend on it.
/// </summary>
public class Adaptation
{
    /// <summary>Canonical id 1..9 (same numbering as <see cref="GalpinAdaptationType"/>).</summary>
    public int Id { get; set; }
    public GalpinAdaptationType Type { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = string.Empty;
    /// <summary>Sessions per week that count as covered (0 = no target).</summary>
    public int TargetSessionsPerWeek { get; set; }
    public List<ExerciseTypeAdaptation> ExerciseTypeAdaptations { get; set; } = [];
}

/// <summary>
/// Canonical mapping from an exercise type to the adaptations it trains.
/// Seeded heuristically from names/tags/muscles and editable per type.
/// </summary>
public class ExerciseTypeAdaptation
{
    public int Id { get; set; }
    public int ExerciseTypeId { get; set; }
    public ExerciseType ExerciseType { get; set; } = null!;
    public int AdaptationId { get; set; }
    public Adaptation Adaptation { get; set; } = null!;
    public AdaptationRole Role { get; set; } = AdaptationRole.Primary;
}

/// <summary>
/// Per-session override of the adaptations an exercise trained, declared by the
/// user's intention (a box jump used as cool-down trains muscular endurance,
/// not power). Without an override the type mapping is used.
/// </summary>
public class ExerciseAdaptation
{
    public int Id { get; set; }
    public Guid ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int AdaptationId { get; set; }
    public Adaptation Adaptation { get; set; } = null!;
}

public enum AdaptationRole
{
    Primary,
    Secondary
}
