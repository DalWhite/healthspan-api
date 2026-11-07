#nullable enable
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Sessions;

/// <summary>
/// Canonical state values for <see cref="GuidedSessionResource.State"/>.
/// There is no server-side "paused"/"abandoned" state in the MVP.
/// </summary>
public static class GuidedSessionStates
{
    public const string Active = "active";
    public const string Completed = "completed";
}

/// <summary>
/// Full guided-session snapshot returned by POST /sessions/from-plan (201 created
/// / 200 resumed) and GET /sessions/{sessionId} (200, ETag "v{version}").
/// A guided session is today's <c>Workout</c> (uniqueness per user/day via
/// DailySessionKey); there is no separate session table.
/// The <see cref="Slots"/> array is a snapshot frozen at creation — later routine
/// edits never mutate an active session. <see cref="Progress"/> is server-derived
/// from committed sets; live rep counters never reach the server (sync
/// granularity is one committed set).
/// </summary>
public sealed class GuidedSessionResource
{
    /// <summary>Session id = Workout.Id (GUID).</summary>
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; set; }

    /// <summary>Routine the session was started from (= Routine.Id).</summary>
    [JsonPropertyName("routineId")]
    public Guid RoutineId { get; set; }

    /// <summary>Snapshot template (= WorkoutTemplate.Id).</summary>
    [JsonPropertyName("templateId")]
    public int TemplateId { get; set; }

    /// <summary>
    /// Snapshot flag taken at creation (Routine.TrainerId != null at the time);
    /// later plan edits never change it.
    /// </summary>
    [JsonPropertyName("coachSigned")]
    public bool CoachSigned { get; set; }

    /// <summary>Coach display name for the "Plan signed by {coach}" badge. Null when the routine has no trainer.</summary>
    [JsonPropertyName("coachName")]
    public string? CoachName { get; set; }

    /// <summary>One of <see cref="GuidedSessionStates"/> ("active" | "completed").</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = GuidedSessionStates.Active;

    /// <summary>
    /// Server-computed version: 1 + committed sets + completions. Informational;
    /// feeds the GET ETag "v{version}". No If-Match on session endpoints —
    /// idempotency is guaranteed by opId dedup on POST /sync.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>UTC ISO 8601 ("Z"). = Workout.startedAt (creation time of the from-plan call).</summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>UTC ISO 8601 ("Z"). Null while the session is active; set by POST /workouts/{id}/complete.</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Planned slots, ordered by WorkoutTemplateExercise.Order (1..n, flat; no blocks/supersets in the MVP).</summary>
    [JsonPropertyName("slots")]
    public List<GuidedSessionSlotResource> Slots { get; set; } = [];

    /// <summary>One derived row per slot; slotId aligns 1:1 with Slots[].slotId.</summary>
    [JsonPropertyName("progress")]
    public List<GuidedSessionProgressResource> Progress { get; set; } = [];

    /// <summary>Aggregates over all slots.</summary>
    [JsonPropertyName("totals")]
    public GuidedSessionTotalsResource Totals { get; set; } = new();
}

/// <summary>
/// One planned exercise slot copied verbatim into the session snapshot at creation.
/// </summary>
public sealed class GuidedSessionSlotResource
{
    /// <summary>= WorkoutTemplateExercise.Id (FK of the planned slot; target of complete_set/undo_set ops).</summary>
    [JsonPropertyName("slotId")]
    public int SlotId { get; set; }

    /// <summary>Display order, 1..n.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>Exercise type of the slot (= ExerciseType.Id).</summary>
    [JsonPropertyName("exerciseTypeId")]
    public int ExerciseTypeId { get; set; }

    /// <summary>Exercise display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Route to the exercise image (→ /api/exercises/{id}/image). Null → FE shows a
    /// placeholder; never a broken image.
    /// </summary>
    [JsonPropertyName("imageRoute")]
    public string? ImageRoute { get; set; }

    /// <summary>Planned sets = expected number of Exercise rows for this slot.</summary>
    [JsonPropertyName("sets")]
    public int Sets { get; set; }

    /// <summary>Target reps per set.</summary>
    [JsonPropertyName("reps")]
    public int Reps { get; set; }

    /// <summary>Planned load, always in kg.</summary>
    [JsonPropertyName("loadKg")]
    public double LoadKg { get; set; }

    /// <summary>Rest in seconds. The countdown is FE-local; there is no server timer.</summary>
    [JsonPropertyName("restSeconds")]
    public int RestSeconds { get; set; }

    /// <summary>
    /// Slot note (= WorkoutTemplateExercise.Notes). Null → FE falls back to
    /// ExerciseType.Description.
    /// </summary>
    [JsonPropertyName("tip")]
    public string? Tip { get; set; }

    /// <summary>Muscle-group slugs for the silhouette diagram.</summary>
    [JsonPropertyName("muscles")]
    public GuidedSessionSlotMusclesResource Muscles { get; set; } = new();
}

/// <summary>
/// Muscle groups for a slot, as MuscleGroup slugs. Primary = filled,
/// secondary = outline; MuscleSide picks the front/back silhouette.
/// </summary>
public sealed class GuidedSessionSlotMusclesResource
{
    [JsonPropertyName("primary")]
    public List<string> Primary { get; set; } = [];

    [JsonPropertyName("secondary")]
    public List<string> Secondary { get; set; } = [];
}

/// <summary>
/// Derived per-slot progress. Computed server-side from committed Exercise rows;
/// never persisted.
/// </summary>
public sealed class GuidedSessionProgressResource
{
    /// <summary>Matches GuidedSessionSlotResource.SlotId.</summary>
    [JsonPropertyName("slotId")]
    public int SlotId { get; set; }

    /// <summary>Count of session Exercise rows with SourceTemplateExerciseId == SlotId.</summary>
    [JsonPropertyName("setsDone")]
    public int SetsDone { get; set; }

    /// <summary>
    /// Reps of the most recently committed set for the slot. Null when
    /// SetsDone == 0 (null != 0 metrics rule).
    /// </summary>
    [JsonPropertyName("repsLast")]
    public int? RepsLast { get; set; }

    /// <summary>True when SetsDone >= the slot's planned Sets.</summary>
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

/// <summary>Session-wide set counters (sum over slots).</summary>
public sealed class GuidedSessionTotalsResource
{
    [JsonPropertyName("setsPlanned")]
    public int SetsPlanned { get; set; }

    [JsonPropertyName("setsDone")]
    public int SetsDone { get; set; }
}
