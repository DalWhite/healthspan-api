#nullable enable
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Sync;

/// <summary>
/// Vocabulary for guided-session operations carried over the existing POST /sync
/// envelope (<see cref="SyncOperationPayload"/>). The envelope itself is NOT
/// changed: an op arrives as entity="guided_session", action=one of the action
/// constants below, and a JSON <c>payload</c> deserialized into the matching
/// payload DTO in this file. <c>opId</c> stays a Guid (FE generates UUID v4);
/// idempotency/dedup is by opId, and offline client ids resolve via the
/// envelope's existing idMap entries — none of those shapes are altered here.
/// </summary>
public static class GuidedSessionSyncActions
{
    /// <summary>Value of <see cref="SyncOperationPayload.Entity"/> for guided-session ops.</summary>
    public const string Entity = "guided_session";

    /// <summary>Commits one set: materializes a single Exercise row lineage-linked to the plan slot.</summary>
    public const string CompleteSet = "complete_set";

    /// <summary>Soft-deletes exactly the last committed Exercise row of the slot (the MVP's only reps correction).</summary>
    public const string UndoSet = "undo_set";
}

/// <summary>
/// JSON payload of a <c>guided_session / complete_set</c> op. One op == one
/// committed set (live rep counters never reach the server). Applying it
/// materializes one Exercise row with SourceTemplateExerciseId == SlotId
/// (lineage → adherence for free), Series intact (=1), and bumps the session
/// version by +1. Replay with the same opId is deduped (no double row).
/// </summary>
public sealed class GuidedSessionCompleteSetPayload
{
    /// <summary>
    /// Server-side session id (= sessionId from the snapshot = Workout.Id).
    /// Used when online. Mutually exclusive with <see cref="ClientWorkoutId"/>:
    /// exactly one of the two must be present.
    /// </summary>
    [JsonPropertyName("sessionWorkoutId")]
    public Guid? SessionWorkoutId { get; set; }

    /// <summary>
    /// FE-generated session id (the clientSessionId queued from the minute zero,
    /// UUID v4). Used when offline: resolved server-side to the real workout via
    /// the idMap entry returned by POST /sync. Mutually exclusive with
    /// <see cref="SessionWorkoutId"/>: exactly one of the two must be present.
    /// </summary>
    [JsonPropertyName("clientWorkoutId")]
    public Guid? ClientWorkoutId { get; set; }

    /// <summary>
    /// Planned slot this set belongs to (= WorkoutTemplateExercise.Id from the
    /// frozen snapshot). If it is not in the session's snapshot the op fails
    /// with failed.reason "slot_not_in_plan"; if the slot already has all its
    /// planned sets committed, "slot_all_sets_done".
    /// </summary>
    [JsonPropertyName("slotId")]
    public int SlotId { get; set; }

    /// <summary>Reps of the local counter at commit time (required, committed value).</summary>
    [JsonPropertyName("reps")]
    public int Reps { get; set; }

    /// <summary>Weight performed, in kg. FE preloads it from slots[i].loadKg.</summary>
    [JsonPropertyName("weightKg")]
    public double WeightKg { get; set; }

    /// <summary>Bodyweight at tap time, in kg. Optional (null when not reported).</summary>
    [JsonPropertyName("bodyweightKg")]
    public double? BodyweightKg { get; set; }

    /// <summary>Free-text note attached to the committed set. Optional (null when absent).</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Client tap instant, epoch milliseconds UTC (wire key is literally
    /// "tap_ts", snake_case). Required in practice. The server ignores it when
    /// applying the op; it is only persisted to compute time-to-log
    /// (applied_at − tap_ts, KPI p50 ≤300ms / p95 ≤1.2s online).
    /// </summary>
    [JsonPropertyName("tap_ts")]
    public long TapTs { get; set; }
}

/// <summary>
/// JSON payload of a <c>guided_session / undo_set</c> op. Deletes exactly the
/// LAST committed Exercise row of the slot (soft-delete). Undoing reps that
/// were never committed is a FE-local reset only and never generates this op.
/// </summary>
public sealed class GuidedSessionUndoSetPayload
{
    /// <summary>
    /// Server-side session id (= Workout.Id). Used when online. Mutually
    /// exclusive with <see cref="ClientWorkoutId"/>: exactly one of the two
    /// must be present (same alternative rule as complete_set).
    /// </summary>
    [JsonPropertyName("sessionWorkoutId")]
    public Guid? SessionWorkoutId { get; set; }

    /// <summary>
    /// FE-generated session id resolved via the idMap returned by POST /sync.
    /// Used when offline. Mutually exclusive with
    /// <see cref="SessionWorkoutId"/>.
    /// </summary>
    [JsonPropertyName("clientWorkoutId")]
    public Guid? ClientWorkoutId { get; set; }

    /// <summary>
    /// Planned slot whose last committed set is removed
    /// (= WorkoutTemplateExercise.Id). Not in the snapshot → failed.reason
    /// "slot_not_in_plan"; nothing to undo → treated as idempotent no-op by
    /// the apply layer (out of scope for these DTOs).
    /// </summary>
    [JsonPropertyName("slotId")]
    public int SlotId { get; set; }
}
