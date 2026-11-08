#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Sessions;

/// <summary>
/// Envelope persisted in Workout.GuidedSnapshotJson. The slots array is copied
/// from the routine template at POST /sessions/from-plan and never mutated
/// afterwards (routine edits and template deletions never touch an active
/// session). Progress is derived, never persisted here.
/// </summary>
public sealed class GuidedSessionSnapshotRecord
{
    [JsonPropertyName("routineId")]
    public Guid RoutineId { get; set; }

    [JsonPropertyName("templateId")]
    public int TemplateId { get; set; }

    /// <summary>Routine.TrainerId != null at creation time.</summary>
    [JsonPropertyName("coachSigned")]
    public bool CoachSigned { get; set; }

    [JsonPropertyName("coachName")]
    public string? CoachName { get; set; }

    /// <summary>UTC creation time of the from-plan call.</summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("slots")]
    public List<GuidedSessionSlotResource> Slots { get; set; } = [];
}

public static class GuidedSessionSnapshotJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GuidedSessionSnapshotRecord snapshot)
        => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>Null on null/blank/corrupt payloads; callers answer 404.</summary>
    public static GuidedSessionSnapshotRecord? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GuidedSessionSnapshotRecord>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
