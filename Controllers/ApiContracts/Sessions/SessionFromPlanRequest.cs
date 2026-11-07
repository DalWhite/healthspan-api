#nullable enable
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Sessions;

/// <summary>
/// Request body for POST /sessions/from-plan: create or resume the guided session
/// for today. Idempotent by DailySessionKey — a second call on the same day
/// returns the existing active session (200 resumed) instead of creating a
/// second Workout. If today's session is already completed the endpoint fails
/// with 409 problem+json, code "session_completed".
/// </summary>
public sealed class SessionFromPlanRequest
{
    /// <summary>Routine to start the session from (= Routine.Id). 404 "routine_not_found" if unknown.</summary>
    [JsonPropertyName("routineId")]
    public Guid RoutineId { get; set; }

    /// <summary>
    /// Explicit template (= WorkoutTemplate.Id). Null → resolved for today via
    /// RoutinePlanService. 404 "template_not_found" if unknown; a template with
    /// no slots fails with 400 "plan_empty".
    /// </summary>
    [JsonPropertyName("workoutTemplateId")]
    public int? WorkoutTemplateId { get; set; }

    /// <summary>
    /// Offline-first FE-generated session id (UUID v4). Ops queued while offline
    /// reference it as clientWorkoutId; the client→server id is resolved through
    /// the idMap entry returned by POST /sync.
    /// </summary>
    [JsonPropertyName("clientSessionId")]
    public Guid ClientSessionId { get; set; }
}
