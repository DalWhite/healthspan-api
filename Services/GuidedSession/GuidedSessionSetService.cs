#nullable enable
using System.Text.Json;
using Controllers.ApiContracts.Sessions;
using Controllers.ApiContracts.Sync;
using healthspan_api.Persistence;
using healthspan_api.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Apply layer for the guided_session actions of POST /sync (complete_set /
/// undo_set), on top of the frozen contracts in
/// Controllers/ApiContracts/Sync/GuidedSessionSyncPayloads.cs. The /sync
/// envelope semantics (opId dedup, idMap, ordering) are untouched here: this
/// only maps one tap op to one materialized Exercise row (complete_set) or
/// one soft delete (undo_set).
/// </summary>
public class GuidedSessionSetService
{
    /// <summary>Exact failed.reason strings the FE branches on.</summary>
    public const string ReasonNotYourSession = "not_your_session";
    public const string ReasonSessionCompleted = "session_completed";
    public const string ReasonSlotAllSetsDone = "slot_all_sets_done";
    public const string ReasonSlotNotInPlan = "slot_not_in_plan";

    /// <summary>Defense-in-depth for a payload that fails to deserialize;
    /// the FE never produces this shape.</summary>
    public const string ReasonPayloadInvalid = "payload_invalid";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly MainDbContext _context;
    private readonly ExercisesRepository _exercises;

    public GuidedSessionSetService(MainDbContext context, ExercisesRepository exercises)
    {
        _context = context;
        _exercises = exercises;
    }

    /// <summary>
    /// Commits one set: materializes one Exercise row with
    /// SourceTemplateExerciseId == slotId (lineage for adherence), Series =
    /// position within the slot, Order monotonically increasing inside the
    /// session. tap_ts inside the payload is deliberately NOT read when
    /// applying (the real SyncOperationPayload envelope has no meta field).
    /// </summary>
    public async Task<(Guid? ServerId, string? Failure)> CompleteSetAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var payload = TryReadPayload<GuidedSessionCompleteSetPayload>(operation.Payload);
        if (payload is null)
        {
            return (null, ReasonPayloadInvalid);
        }

        var session = await ResolveSessionAsync(
            payload.SessionWorkoutId, payload.ClientWorkoutId, userId, cancellationToken);
        if (session is null)
        {
            return (null, ReasonNotYourSession);
        }
        if (session.CompletedAt is not null)
        {
            return (null, ReasonSessionCompleted);
        }

        var slot = await ResolveSlotAsync(session, payload.SlotId, cancellationToken);
        if (slot is null)
        {
            return (null, ReasonSlotNotInPlan);
        }

        // Committed count via the filtered query (soft-deleted rows are
        // invisible), so an undone set frees its slot position again.
        var committed = await _context.Exercises
            .Where(exercise => exercise.WorkoutId == session.Id
                               && exercise.SourceTemplateExerciseId == slot.SlotId)
            .CountAsync(cancellationToken);
        if (committed >= slot.Sets)
        {
            return (null, ReasonSlotAllSetsDone);
        }

        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            ClientEntityId = operation.ClientEntityId,
            WorkoutId = session.Id,
            TypeId = slot.ExerciseTypeId,
            SourceTemplateExerciseId = slot.SlotId,
            Series = committed + 1,
            Order = await NextOrderAsync(session.Id, cancellationToken),
            Repetitions = payload.Reps,
            Weight = payload.WeightKg,
            BodyWeight = payload.BodyweightKg,
            Comments = payload.Notes ?? "",
        };
        _exercises.CalculateComputedValues(exercise, userId);
        _context.Exercises.Add(exercise);

        return (exercise.Id, null);
    }

    /// <summary>
    /// Removes exactly the last committed set of the slot via a soft delete
    /// (Exercise.IsDeleted, hidden from every read path by the global query
    /// filter). Undoing on an empty slot is an applied no-op so replays
    /// converge instead of failing.
    /// </summary>
    public async Task<(Guid? ServerId, string? Failure)> UndoSetAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var payload = TryReadPayload<GuidedSessionUndoSetPayload>(operation.Payload);
        if (payload is null)
        {
            return (null, ReasonPayloadInvalid);
        }

        var session = await ResolveSessionAsync(
            payload.SessionWorkoutId, payload.ClientWorkoutId, userId, cancellationToken);
        if (session is null)
        {
            return (null, ReasonNotYourSession);
        }
        if (session.CompletedAt is not null)
        {
            return (null, ReasonSessionCompleted);
        }

        var slot = await ResolveSlotAsync(session, payload.SlotId, cancellationToken);
        if (slot is null)
        {
            return (null, ReasonSlotNotInPlan);
        }

        var last = await _context.Exercises
            .Where(exercise => exercise.WorkoutId == session.Id
                               && exercise.SourceTemplateExerciseId == slot.SlotId)
            .OrderByDescending(exercise => exercise.Order ?? 0)
            .FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            // Nothing committed to undo: converge as an applied no-op.
            return (null, null);
        }

        last.IsDeleted = true;
        return (last.Id, null);
    }

    /// <summary>
    /// Resolves the session workout, enforcing ownership. sessionWorkoutId
    /// (server id) wins when both are present; clientWorkoutId resolves via
    /// the ClientEntityId stamped by from-plan / offline create. Cross-user
    /// ids never resolve (the global Workouts filter is bypassed explicitly
    /// and ownership is checked against the authenticated user instead).
    /// </summary>
    private async Task<Workout?> ResolveSessionAsync(
        Guid? sessionWorkoutId,
        Guid? clientWorkoutId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (sessionWorkoutId is Guid serverId)
        {
            return await _context.Workouts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    workout => workout.Id == serverId && workout.UserId == userId,
                    cancellationToken);
        }
        if (clientWorkoutId is Guid clientId)
        {
            return await _context.Workouts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    workout => workout.ClientEntityId == clientId && workout.UserId == userId,
                    cancellationToken);
        }
        return null;
    }

    /// <summary>Slot from the frozen snapshot; null when the workout is not a
    /// guided session or the slot is not part of its plan.</summary>
    private static async Task<GuidedSessionSlotResource?> ResolveSlotAsync(
        Workout session,
        int slotId,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken; // snapshot lives in the already-loaded row
        var snapshot = GuidedSessionSnapshotJson.Deserialize(session.GuidedSnapshotJson);
        if (snapshot is null)
        {
            return null;
        }
        return snapshot.Slots.FirstOrDefault(slot => slot.SlotId == slotId);
    }

    /// <summary>Order increases monotonically inside the session, counting
    /// soft-deleted rows, so undone positions are never reused.</summary>
    private async Task<int> NextOrderAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var maxOrder = await _context.Exercises
            .IgnoreQueryFilters()
            .Where(exercise => exercise.WorkoutId == sessionId)
            .Select(exercise => exercise.Order ?? 0)
            .DefaultIfEmpty()
            .MaxAsync(cancellationToken);
        return maxOrder + 1;
    }

    private static TPayload? TryReadPayload<TPayload>(JsonElement payload)
        where TPayload : class
    {
        try
        {
            return payload.Deserialize<TPayload>(PayloadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
