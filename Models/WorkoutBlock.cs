#nullable enable

/// <summary>
/// A group of exercises inside a session (universal training tracker design).
/// Exercises without a block belong to an implicit Normal block, so existing
/// logged workouts keep working unchanged.
/// </summary>
public class WorkoutBlock
{
    public Guid Id { get; set; }
    /// <summary>Offline-first id of the block created on the client.</summary>
    public Guid? ClientEntityId { get; set; }
    public Guid WorkoutId { get; set; }
    public Workout Workout { get; set; } = null!;
    /// <summary>Dense 1..n position inside the workout.</summary>
    public int Order { get; set; }
    public WorkoutBlockType Type { get; set; } = WorkoutBlockType.Normal;
    public string? Label { get; set; }
    /// <summary>Rounds/repeats of the block (supersets, circuits, EMOM, ...).</summary>
    public int? Rounds { get; set; }
    public List<Exercise> Exercises { get; set; } = [];
}

public enum WorkoutBlockType
{
    Normal,
    Superset,
    Circuito,
    EMOM,
    AMRAP,
    Tabata,
    Ronda
}
