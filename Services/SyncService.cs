using System.Text.Json;
using Controllers.ApiContracts.Sync;
using healthspan_api.Persistence;
using Microsoft.EntityFrameworkCore;
using Services;

namespace Services;

/// <summary>
/// Applies the client's offline queue in order, idempotently:
/// OpId is logged, entities carry their ClientEntityId, and replays are
/// answered as deduped instead of creating duplicates.
/// </summary>
public class SyncService
{
    private readonly MainDbContext _context;
    private readonly CurrentUserProvider _userProvider;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        MainDbContext context,
        CurrentUserProvider userProvider,
        ILogger<SyncService> logger)
    {
        _context = context;
        _userProvider = userProvider;
        _logger = logger;
    }

    public async Task<SyncResponse> SyncAsync(
        SyncRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _userProvider.GetCurrentUserId();
        var response = new SyncResponse();

        foreach (var operation in request.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (operation.OpId == Guid.Empty
                || string.IsNullOrWhiteSpace(operation.Entity)
                || string.IsNullOrWhiteSpace(operation.Action))
            {
                response.Failed.Add(new SyncFailure
                {
                    OpId = operation.OpId,
                    Reason = "Malformed sync operation."
                });
                continue;
            }

            var alreadyApplied = await _context.SyncOperations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    entry => entry.UserId == userId && entry.OpId == operation.OpId,
                    cancellationToken);

            if (alreadyApplied is not null)
            {
                response.Deduped++;
                if (alreadyApplied.ServerEntityId is not null
                    && operation.ClientEntityId is not null)
                {
                    response.IdMap.Add(new SyncIdMap
                    {
                        ClientId = operation.ClientEntityId.Value,
                        ServerId = alreadyApplied.ServerEntityId.Value
                    });
                }

                continue;
            }

            try
            {
                var result = await ApplyAsync(operation, userId, cancellationToken);
                if (result.Failure is not null)
                {
                    response.Failed.Add(new SyncFailure
                    {
                        OpId = operation.OpId,
                        Reason = result.Failure
                    });
                    continue;
                }

                response.Applied++;
                if (result.ServerId is not null && operation.ClientEntityId is not null)
                {
                    response.IdMap.Add(new SyncIdMap
                    {
                        ClientId = operation.ClientEntityId.Value,
                        ServerId = result.ServerId.Value
                    });
                }

                _context.SyncOperations.Add(new SyncOperation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OpId = operation.OpId,
                    Entity = operation.Entity,
                    Action = operation.Action,
                    ClientEntityId = operation.ClientEntityId,
                    ServerEntityId = result.ServerId,
                    AppliedAtUtc = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // A failed write poisons the tracked context for the rest of
                // the batch: roll it back and report the op as failed. The
                // client drops it after the first attempt (see SyncEngine).
                _logger.LogWarning(
                    ex,
                    "Sync operation {OpId} ({Entity}/{Action}) failed for user {UserId}.",
                    operation.OpId,
                    operation.Entity,
                    operation.Action,
                    userId);
                _context.ChangeTracker.Clear();
                response.Failed.Add(new SyncFailure
                {
                    OpId = operation.OpId,
                    Reason = "The operation could not be saved."
                });
            }
        }

        return response;
    }

    private async Task<(Guid? ServerId, string? Failure)> ApplyAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var entity = operation.Entity.Trim().ToLowerInvariant();
        var action = operation.Action.Trim().ToLowerInvariant();

        return (entity, action) switch
        {
            ("workout", "create") => await CreateWorkoutAsync(operation, userId, cancellationToken),
            ("workout", "update") => await UpdateWorkoutAsync(operation, userId, cancellationToken),
            ("workout", "delete") => await DeleteWorkoutAsync(operation, userId, cancellationToken),
            ("exercise", "create") => await CreateExerciseAsync(operation, userId, cancellationToken),
            ("exercise", "update") => await UpdateExerciseAsync(operation, userId, cancellationToken),
            ("exercise", "delete") => await DeleteExerciseAsync(operation, userId, cancellationToken),
            ("workout_block", "create") => await CreateBlockAsync(operation, userId, cancellationToken),
            ("workout_block", "update") => await UpdateBlockAsync(operation, userId, cancellationToken),
            ("workout_block", "delete") => await DeleteBlockAsync(operation, userId, cancellationToken),
            _ => (null, $"Unknown entity/action: {operation.Entity}/{operation.Action}")
        };
    }

    private async Task<(Guid? ServerId, string? Failure)> CreateWorkoutAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        if (operation.ClientEntityId is null)
        {
            return (null, "Missing client entity id.");
        }

        var clientId = operation.ClientEntityId.Value;
        var existing = await _context.Workouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                workout => workout.UserId == userId
                           && workout.ClientEntityId == clientId,
                cancellationToken);
        if (existing is not null)
        {
            return (existing.Id, null);
        }

        var date = TryGetDate(operation.Payload, "date")
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var type = TryGetString(operation.Payload, "type") ?? "Training";
        var workoutId = Guid.NewGuid();

        var workout = new Workout
        {
            Id = workoutId,
            ClientEntityId = clientId,
            UserId = userId,
            Date = date.ToDateTime(
                date == DateOnly.FromDateTime(DateTime.Today)
                    ? TimeOnly.FromDateTime(DateTime.Now)
                    : TimeOnly.MinValue),
            Type = string.IsNullOrWhiteSpace(type) ? "Training" : type.Trim(),
            DailySessionKey = WorkoutDailySessionKey.CreateForSession(
                userId,
                date,
                workoutId),
            Exercises = []
        };

        ApplyWorkoutTracker(workout, operation.Payload);
        _context.Workouts.Add(workout);

        return (workoutId, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> UpdateWorkoutAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var workout = await ResolveWorkoutAsync(operation, userId, cancellationToken);
        if (workout is null)
        {
            return (null, "Workout not found.");
        }

        var date = TryGetDate(operation.Payload, "date");
        if (date is not null)
        {
            workout.Date = date.Value.ToDateTime(
                date.Value == DateOnly.FromDateTime(DateTime.Today)
                    ? TimeOnly.FromDateTime(DateTime.Now)
                    : TimeOnly.MinValue);
            workout.DailySessionKey = WorkoutDailySessionKey.CreateForSession(
                workout.UserId,
                date.Value,
                workout.Id);
        }

        var type = TryGetString(operation.Payload, "type");
        if (!string.IsNullOrWhiteSpace(type))
        {
            workout.Type = type.Trim();
        }

        ApplyWorkoutTracker(workout, operation.Payload);

        return (workout.Id, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> DeleteWorkoutAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var workout = await _context.Workouts
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Exercises)
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId
                             && (candidate.ClientEntityId == operation.ClientEntityId
                                 || candidate.Id == operation.ClientEntityId),
                cancellationToken);
        if (workout is null)
        {
            return (operation.ClientEntityId, null);
        }

        var workoutId = workout.Id;
        _context.Exercises.RemoveRange(workout.Exercises);
        _context.Workouts.Remove(workout);

        // The exercises' sync logs must go too: their client ids are freed
        // and a later create with the same id must not be treated as dedup.
        var exerciseIds = workout.Exercises.Select(exercise => exercise.Id).ToList();
        _context.SyncOperations
            .RemoveRange(_context.SyncOperations
                .IgnoreQueryFilters()
                .Where(entry => entry.UserId == userId
                                && entry.Entity == "exercise"
                                && entry.ServerEntityId != null
                                && exerciseIds.Contains(entry.ServerEntityId.Value)));

        return (workoutId, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> CreateExerciseAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        if (operation.ClientEntityId is null)
        {
            return (null, "Missing client entity id.");
        }

        var clientId = operation.ClientEntityId.Value;
        var existing = await _context.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                exercise => exercise.Workout.UserId == userId
                            && exercise.ClientEntityId == clientId,
                cancellationToken);
        if (existing is not null)
        {
            return (existing.Id, null);
        }

        var workoutId = await ResolveWorkoutIdAsync(operation.Payload, userId, cancellationToken);
        if (workoutId is null)
        {
            return (null, "Parent workout not found.");
        }

        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            ClientEntityId = clientId,
            WorkoutId = workoutId,
            TypeId = TryGetInt(operation.Payload, "typeId"),
            Series = TryGetInt(operation.Payload, "series"),
            Repetitions = TryGetInt(operation.Payload, "repetitions"),
            Weight = TryGetDouble(operation.Payload, "weight"),
            Comments = TryGetString(operation.Payload, "comments") ?? "",
            Tags = TryGetStringList(operation.Payload, "tags") ?? []
        };

        var weightType = TryGetString(operation.Payload, "weightType");
        var bodyWeight = TryGetDouble(operation.Payload, "bodyWeight");
        var addedWeight = TryGetDouble(operation.Payload, "addedWeight");
        if (weightType == "BodyWeight")
        {
            exercise.BodyWeight = bodyWeight ?? 1;
        }
        else if (weightType == "AddedWeight")
        {
            exercise.AddedWeight = addedWeight ?? 0;
        }
        else if (bodyWeight is not null)
        {
            exercise.BodyWeight = bodyWeight;
        }
        else if (addedWeight is not null)
        {
            exercise.AddedWeight = addedWeight;
        }

        var blockId = await ResolveBlockIdAsync(operation.Payload, userId, cancellationToken);
        if (blockId is not null)
        {
            exercise.BlockId = blockId;
        }

        exercise.Order = TryGetInt(operation.Payload, "order");
        exercise.Attempts = TryGetInt(operation.Payload, "attempts");
        exercise.CompletedAttempts = TryGetInt(operation.Payload, "completedAttempts");
        exercise.QualityIntent = TryGetString(operation.Payload, "qualityIntent");
        if (TrainingTrackerParser.TryLoadType(
                TryGetString(operation.Payload, "loadType"),
                out var loadType))
        {
            exercise.LoadType = loadType;
        }

        await ApplyExerciseAdaptationsAsync(
            exercise,
            TryGetStringList(operation.Payload, "adaptations"),
            cancellationToken);

        _context.Exercises.Add(exercise);
        return (exercise.Id, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> UpdateExerciseAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var exercise = await _context.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Workout.UserId == userId
                             && (candidate.ClientEntityId == operation.ClientEntityId
                                 || candidate.Id == operation.ClientEntityId),
                cancellationToken);
        if (exercise is null)
        {
            return (null, "Exercise not found.");
        }

        var payload = operation.Payload;
        if (payload.TryGetProperty("series", out _))
        {
            exercise.Series = TryGetInt(payload, "series");
        }

        if (payload.TryGetProperty("repetitions", out _))
        {
            exercise.Repetitions = TryGetInt(payload, "repetitions");
        }

        if (payload.TryGetProperty("weight", out _))
        {
            exercise.Weight = TryGetDouble(payload, "weight");
        }

        if (payload.TryGetProperty("bodyWeight", out _))
        {
            exercise.BodyWeight = TryGetDouble(payload, "bodyWeight");
        }

        if (payload.TryGetProperty("addedWeight", out _))
        {
            exercise.AddedWeight = TryGetDouble(payload, "addedWeight");
        }

        if (payload.TryGetProperty("comments", out _))
        {
            exercise.Comments = TryGetString(payload, "comments") ?? "";
        }

        if (payload.TryGetProperty("tags", out _))
        {
            var tags = TryGetStringList(payload, "tags");
            if (tags is { Count: > 0 })
            {
                exercise.Tags = tags;
            }
        }

        if (payload.TryGetProperty("blockId", out _)
            || payload.TryGetProperty("blockClientId", out _))
        {
            var blockId = await ResolveBlockIdAsync(payload, userId, cancellationToken);
            if (blockId is not null)
            {
                exercise.BlockId = blockId;
            }
        }

        if (payload.TryGetProperty("order", out _))
        {
            exercise.Order = TryGetInt(payload, "order");
        }

        if (payload.TryGetProperty("attempts", out _))
        {
            exercise.Attempts = TryGetInt(payload, "attempts");
        }

        if (payload.TryGetProperty("completedAttempts", out _))
        {
            exercise.CompletedAttempts = TryGetInt(payload, "completedAttempts");
        }

        if (payload.TryGetProperty("qualityIntent", out _))
        {
            exercise.QualityIntent = TryGetString(payload, "qualityIntent");
        }

        if (payload.TryGetProperty("loadType", out _)
            && TrainingTrackerParser.TryLoadType(TryGetString(payload, "loadType"), out var loadType))
        {
            exercise.LoadType = loadType;
        }

        if (payload.TryGetProperty("adaptations", out _))
        {
            await ApplyExerciseAdaptationsAsync(
                exercise,
                TryGetStringList(payload, "adaptations"),
                cancellationToken);
        }

        return (exercise.Id, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> DeleteExerciseAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var exercise = await _context.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Workout.UserId == userId
                             && (candidate.ClientEntityId == operation.ClientEntityId
                                 || candidate.Id == operation.ClientEntityId),
                cancellationToken);
        if (exercise is null)
        {
            return (operation.ClientEntityId, null);
        }

        var exerciseId = exercise.Id;
        _context.Exercises.Remove(exercise);
        _context.SyncOperations
            .RemoveRange(_context.SyncOperations
                .IgnoreQueryFilters()
                .Where(entry => entry.UserId == userId
                                && entry.Entity == "exercise"
                                && entry.ServerEntityId != null
                                && entry.ServerEntityId.Value == exerciseId));

        return (exerciseId, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> CreateBlockAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        if (operation.ClientEntityId is null)
        {
            return (null, "Missing client entity id.");
        }

        var clientId = operation.ClientEntityId.Value;
        var existing = await _context.WorkoutBlocks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                block => block.Workout.UserId == userId
                         && block.ClientEntityId == clientId,
                cancellationToken);
        if (existing is not null)
        {
            return (existing.Id, null);
        }

        var workoutId = await ResolveWorkoutIdAsync(operation.Payload, userId, cancellationToken);
        if (workoutId is null)
        {
            return (null, "Parent workout not found.");
        }

        if (!TrainingTrackerParser.TryBlockType(
                TryGetString(operation.Payload, "type"),
                out var blockType))
        {
            return (null, "Unknown block type.");
        }

        var block = new WorkoutBlock
        {
            Id = Guid.NewGuid(),
            ClientEntityId = clientId,
            WorkoutId = workoutId.Value,
            Order = TryGetInt(operation.Payload, "order") ?? 1,
            Type = blockType,
            Label = TryGetString(operation.Payload, "label"),
            Rounds = TryGetInt(operation.Payload, "rounds")
        };

        _context.WorkoutBlocks.Add(block);
        return (block.Id, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> UpdateBlockAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var block = await ResolveBlockAsync(operation, userId, cancellationToken);
        if (block is null)
        {
            return (null, "Block not found.");
        }

        var payload = operation.Payload;

        if (payload.TryGetProperty("type", out _)
            && TrainingTrackerParser.TryBlockType(TryGetString(payload, "type"), out var blockType))
        {
            block.Type = blockType;
        }

        if (payload.TryGetProperty("order", out _))
        {
            block.Order = TryGetInt(payload, "order") ?? block.Order;
        }

        if (payload.TryGetProperty("label", out _))
        {
            block.Label = TryGetString(payload, "label");
        }

        if (payload.TryGetProperty("rounds", out _))
        {
            block.Rounds = TryGetInt(payload, "rounds");
        }

        return (block.Id, null);
    }

    private async Task<(Guid? ServerId, string? Failure)> DeleteBlockAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
    {
        var block = await ResolveBlockAsync(operation, userId, cancellationToken);
        if (block is null)
        {
            return (operation.ClientEntityId, null);
        }

        var blockId = block.Id;

        // Exercises that belonged to the block survive as an implicit Normal block.
        foreach (var exercise in block.Exercises ?? [])
        {
            exercise.BlockId = null;
        }

        _context.WorkoutBlocks.Remove(block);
        _context.SyncOperations
            .RemoveRange(_context.SyncOperations
                .IgnoreQueryFilters()
                .Where(entry => entry.UserId == userId
                                && entry.Entity == "workout_block"
                                && entry.ServerEntityId != null
                                && entry.ServerEntityId.Value == blockId));

        return (blockId, null);
    }

    private async Task<WorkoutBlock?> ResolveBlockAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
        => await _context.WorkoutBlocks
            .IgnoreQueryFilters()
            .Include(block => block.Exercises)
            .FirstOrDefaultAsync(
                candidate => candidate.Workout.UserId == userId
                             && (candidate.ClientEntityId == operation.ClientEntityId
                                 || candidate.Id == operation.ClientEntityId),
                cancellationToken);

    private async Task<Guid?> ResolveBlockIdAsync(
        JsonElement payload,
        string userId,
        CancellationToken cancellationToken)
    {
        // Same rule as the parent workout: the client id wins, because a block
        // created offline can land in the same batch as its exercises.
        var clientRaw = TryGetString(payload, "blockClientId");
        if (clientRaw is not null && Guid.TryParse(clientRaw, out var clientGuid))
        {
            var byClient = await _context.WorkoutBlocks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    block => block.Workout.UserId == userId
                             && block.ClientEntityId == clientGuid,
                    cancellationToken);
            if (byClient is not null)
            {
                return byClient.Id;
            }
        }

        var raw = TryGetString(payload, "blockId");
        if (raw is null || !Guid.TryParse(raw, out var blockId))
        {
            return null;
        }

        var block = await _context.WorkoutBlocks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.Workout.UserId == userId
                             && (candidate.Id == blockId
                                 || candidate.ClientEntityId == blockId),
                cancellationToken);

        return block?.Id;
    }

    private static void ApplyWorkoutTracker(Workout workout, JsonElement payload)
    {
        if (payload.TryGetProperty("goal", out _))
        {
            if (TrainingTrackerParser.TryAdaptation(TryGetString(payload, "goal"), out var goal))
            {
                workout.Goal = goal;
            }
        }

        if (payload.TryGetProperty("secondaryGoals", out _))
        {
            var parsed = (TryGetStringList(payload, "secondaryGoals") ?? [])
                .Select(name => TrainingTrackerParser.TryAdaptation(name, out var adaptation)
                    ? adaptation
                    : (GalpinAdaptationType?)null)
                .Where(adaptation => adaptation is not null)
                .Select(adaptation => adaptation!.Value)
                .Distinct()
                .ToList();
            workout.SecondaryGoals = parsed;
        }

        if (payload.TryGetProperty("modality", out _))
        {
            if (TrainingTrackerParser.TryModality(TryGetString(payload, "modality"), out var modality))
            {
                workout.Modality = modality;
            }
        }

        if (payload.TryGetProperty("perceivedFatigue", out _))
        {
            var fatigue = TryGetInt(payload, "perceivedFatigue");
            if (fatigue is >= 1 and <= 5)
            {
                workout.PerceivedFatigue = fatigue;
            }
        }
    }

    private async Task ApplyExerciseAdaptationsAsync(
        Exercise exercise,
        List<string>? adaptationNames,
        CancellationToken cancellationToken)
    {
        var existing = await _context.ExerciseAdaptations
            .IgnoreQueryFilters()
            .Where(adaptation => adaptation.ExerciseId == exercise.Id)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _context.ExerciseAdaptations.RemoveRange(existing);
        }

        if (adaptationNames is not { Count: > 0 })
        {
            return;
        }

        var wanted = adaptationNames
            .Select(name => TrainingTrackerParser.TryAdaptation(name, out var adaptation)
                ? adaptation
                : (GalpinAdaptationType?)null)
            .Where(adaptation => adaptation is not null)
            .Select(adaptation => adaptation!.Value)
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var adaptations = await _context.Adaptations
            .Where(adaptation => wanted.Contains(adaptation.Type))
            .ToListAsync(cancellationToken);

        exercise.Adaptations = adaptations
            .Select(adaptation => new ExerciseAdaptation
            {
                ExerciseId = exercise.Id,
                AdaptationId = adaptation.Id
            })
            .ToList();
    }

    private async Task<Workout?> ResolveWorkoutAsync(
        SyncOperationPayload operation,
        string userId,
        CancellationToken cancellationToken)
        => await _context.Workouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId
                             && (candidate.ClientEntityId == operation.ClientEntityId
                                 || candidate.Id == operation.ClientEntityId),
                cancellationToken);

    private async Task<Guid?> ResolveWorkoutIdAsync(
        JsonElement payload,
        string userId,
        CancellationToken cancellationToken)
    {
        // Prefer the client id: it is always present, even when the parent
        // workout was created offline and its create op lands in the same
        // batch (the server id is only known after that op is applied).
        var clientRaw = TryGetString(payload, "workoutClientId");
        if (clientRaw is not null && Guid.TryParse(clientRaw, out var clientGuid))
        {
            var byClient = await _context.Workouts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    candidate => candidate.UserId == userId
                                 && candidate.ClientEntityId == clientGuid,
                    cancellationToken);
            if (byClient is not null)
            {
                return byClient.Id;
            }
        }

        var raw = TryGetString(payload, "workoutId");
        if (raw is null || !Guid.TryParse(raw, out var workoutId))
        {
            return null;
        }

        var workout = await _context.Workouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId
                             && (candidate.Id == workoutId
                                 || candidate.ClientEntityId == workoutId),
                cancellationToken);

        // Return the SERVER id: the payload may reference a client-generated
        // id from the same offline batch.
        return workout?.Id;
    }

    private static string? TryGetString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static double? TryGetDouble(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static List<string>? TryGetStringList(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static DateOnly? TryGetDate(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateOnly.TryParse(text, out var dateOnly))
            {
                return dateOnly;
            }

            if (DateTime.TryParse(text, out var dateTime))
            {
                return DateOnly.FromDateTime(dateTime);
            }
        }

        return null;
    }
}
