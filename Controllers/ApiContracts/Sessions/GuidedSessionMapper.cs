#nullable enable
namespace Controllers.ApiContracts.Sessions;

/// <summary>
/// Builds the canonical GuidedSessionResource from a Workout row plus its frozen
/// snapshot. Progress/totals/version are always server-derived:
/// version = 1 + committed sets + completions.
/// </summary>
public static class GuidedSessionMapper
{
    public static GuidedSessionResource BuildResource(
        Workout session,
        GuidedSessionSnapshotRecord snapshot)
    {
        var slotIds = snapshot.Slots.Select(slot => slot.SlotId).ToHashSet();

        // A committed set is a session Exercise row whose lineage points at one
        // of the frozen slots (complete_set via sync). Planned apply-template
        // rows share the same lineage; guided sessions never pre-create them.
        var committed = (session.Exercises ?? [])
            .Where(exercise => exercise.SourceTemplateExerciseId is int lineage
                && slotIds.Contains(lineage))
            .ToList();

        var progress = new List<GuidedSessionProgressResource>(snapshot.Slots.Count);
        foreach (var slot in snapshot.Slots)
        {
            var slotSets = committed
                .Where(exercise => exercise.SourceTemplateExerciseId == slot.SlotId)
                .ToList();

            // "Most recently committed" = highest dense Order stamped by the
            // apply layer; Id tiebreak keeps it deterministic for equal rows.
            var last = slotSets
                .OrderBy(exercise => exercise.Order ?? 0)
                .ThenBy(exercise => exercise.Id)
                .LastOrDefault();

            progress.Add(new GuidedSessionProgressResource
            {
                SlotId = slot.SlotId,
                SetsDone = slotSets.Count,
                RepsLast = last?.Repetitions,
                Done = slotSets.Count >= slot.Sets,
            });
        }

        var completed = session.CompletedAt is not null;

        return new GuidedSessionResource
        {
            SessionId = session.Id,
            RoutineId = snapshot.RoutineId,
            TemplateId = snapshot.TemplateId,
            CoachSigned = snapshot.CoachSigned,
            CoachName = snapshot.CoachName,
            State = completed ? GuidedSessionStates.Completed : GuidedSessionStates.Active,
            Version = 1 + committed.Count + (completed ? 1 : 0),
            StartedAt = snapshot.StartedAt,
            CompletedAt = session.CompletedAt,
            Slots = snapshot.Slots,
            Progress = progress,
            Totals = new GuidedSessionTotalsResource
            {
                SetsPlanned = snapshot.Slots.Sum(slot => slot.Sets),
                SetsDone = committed.Count,
            },
        };
    }

    /// <summary>
    /// Exact ETag value from the closed contract: "v{version}" (strong tag).
    /// </summary>
    public static string ETagOf(int version) => $"\"v{version}\"";

    /// <summary>
    /// Strong/weak comparison per RFC 9110 for If-None-Match → 304.
    /// </summary>
    public static bool IfNoneMatchMatches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        var expected = etag.TrimStart('W', '/');
        return ifNoneMatch.Split(',', StringSplitOptions.TrimEntries)
            .Any(candidate => candidate == "*" || candidate.TrimStart('W', '/') == expected);
    }
}
