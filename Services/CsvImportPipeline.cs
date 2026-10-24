using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using healthspan_api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;

/// <summary>
/// Batched CSV import pipeline (t_c1f8e6ac). Replaces the per-row pipeline
/// measured in t_37acc65b (SaveChanges + unindexed history SELECT per row,
/// ~O(N^2)) with:
///   - 1 query + 1 insert for all exercise types,
///   - 1 range query + bulk insert for all daily workout sessions,
///   - 1 grouped MAX query per job to seed per-type 1RM history, then the
///     running max is kept in memory (same semantics as the per-row query),
///   - bulk inserts per batch of 1000 rows (ExecuteInsert on relational
///     providers, batched SaveChanges elsewhere),
///   - idempotency via a deterministic ClientEntityId per row content:
///     reimporting the same CSV skips duplicates instead of duplicating.
/// Runs on a background scope without HttpContext, so every query must use
/// IgnoreQueryFilters + an explicit userId filter (the query filters would
/// dereference a null HttpContext otherwise).
/// </summary>
public sealed class CsvImportPipeline
{
    public const int BatchSize = 1_000;

    private readonly MainDbContext _db;
    private readonly WorkoutsRepository _workouts;
    private readonly string _userId;
    private readonly ILogger<CsvImportPipeline> _logger;

    public CsvImportPipeline(
        MainDbContext db,
        WorkoutsRepository workouts,
        string userId,
        ILogger<CsvImportPipeline> logger)
    {
        _db = db;
        _workouts = workouts;
        _userId = userId;
        _logger = logger;
    }

    public async Task RunAsync(
        List<CsvImportRow> rows,
        CsvImportJob job,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var swPhase = System.Diagnostics.Stopwatch.StartNew();
        var typeByName = await LoadExerciseTypesAsync(rows, cancellationToken);
        PhaseLog($"[phase] types: {swPhase.ElapsedMilliseconds} ms"); swPhase.Restart();

        var dates = rows
            .Select(row => DateOnly.FromDateTime(row.Date))
            .Distinct()
            .ToList();
        var workoutIdByDate = await _workouts.GetOrCreateWorkoutSessionsBulkAsync(
            _userId,
            dates,
            "Training",
            cancellationToken);
        PhaseLog($"[phase] workouts ({dates.Count} dates): {swPhase.ElapsedMilliseconds} ms"); swPhase.Restart();

        var runningMaxByType = await LoadHistoricalMaxByTypeAsync(cancellationToken);
        PhaseLog($"[phase] maxseed: {swPhase.ElapsedMilliseconds} ms"); swPhase.Restart();

        // One transaction around the whole batch loop: without it MySQL fsyncs
        // per batch (probe: 10k inserts 7.2 s no-tx vs 3.1 s with tx). A failed
        // row does not abort a MySQL transaction, so the per-row fallback in
        // ProcessBatchAsync still commits the good rows.
        using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            foreach (var batch in rows.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessBatchAsync(
                    batch,
                    job,
                    typeByName,
                    workoutIdByDate,
                    runningMaxByType,
                    cancellationToken);
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        PhaseLog($"[phase] batches ({rows.Count} rows): {swPhase.ElapsedMilliseconds} ms (dedup {_dedupMs} ms, insert {_insertMs} ms)");
    }

    private static void PhaseLog(string message)
    {
        var path = Environment.GetEnvironmentVariable("CSV_IMPORT_PHASE_LOG");
        if (!string.IsNullOrEmpty(path))
        {
            lock (typeof(CsvImportPipeline)) { File.AppendAllText(path, message + Environment.NewLine); }
        }
    }

    private long _dedupMs;
    private long _insertMs;

    private async Task<Dictionary<string, (int Id, string WeightType)>> LoadExerciseTypesAsync(
        List<CsvImportRow> rows,
        CancellationToken cancellationToken)
    {
        var names = rows.Select(row => row.TypeName).Distinct().ToList();

        var existing = await _db.ExerciseTypes
            .IgnoreQueryFilters()
            .Where(type => names.Contains(type.Name))
            .Select(type => new { type.Id, type.Name, type.WeightType })
            .ToListAsync(cancellationToken);

        // MySQL compares names case-insensitively; mirror that in memory so a
        // CSV "bench press" maps to an existing "Bench press" type.
        var byName = new Dictionary<string, (int Id, string WeightType)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var type in existing)
        {
            byName.TryAdd(type.Name, (type.Id, type.WeightType));
        }

        var missing = names.Where(name => !byName.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            var created = missing
                .Select(name => new ExerciseType { Name = name })
                .ToList();
            _db.ExerciseTypes.AddRange(created);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var type in created)
            {
                byName[type.Name] = (type.Id, type.WeightType);
            }
        }

        return byName;
    }

    private async Task<Dictionary<int, double>> LoadHistoricalMaxByTypeAsync(
        CancellationToken cancellationToken)
    {
        var maxes = await _db.Exercises
            .IgnoreQueryFilters()
            .Where(exercise =>
                exercise.Workout.UserId == _userId
                && exercise.TypeId != null
                && exercise.MaxRepetition != null)
            .GroupBy(exercise => exercise.TypeId!.Value)
            .Select(group => new
            {
                TypeId = group.Key,
                Max = group.Max(exercise => exercise.MaxRepetition)
            })
            .ToListAsync(cancellationToken);

        return maxes.ToDictionary(
            entry => entry.TypeId,
            entry => entry.Max ?? 0d);
    }

    private async Task ProcessBatchAsync(
        CsvImportRow[] batch,
        CsvImportJob job,
        Dictionary<string, (int Id, string WeightType)> typeByName,
        Dictionary<DateOnly, Guid> workoutIdByDate,
        Dictionary<int, double> runningMaxByType,
        CancellationToken cancellationToken)
    {
        // Dedupe inside the batch first: identical rows share the same
        // deterministic import id.
        var candidates = new List<(CsvImportRow Row, Guid ImportId)>(batch.Length);
        var seenIds = new HashSet<Guid>();
        foreach (var row in batch)
        {
            var importId = ImportEntityId(row);
            if (!seenIds.Add(importId))
            {
                job.AddSkipped(1);
                continue;
            }
            candidates.Add((row, importId));
        }

        // Dedupe against the database: a reimported CSV skips rows that were
        // already imported (idempotency decision from t_37acc65b §6.3).
        var importIds = candidates.Select(c => c.ImportId).ToList();
        var swDedup = System.Diagnostics.Stopwatch.StartNew();
        var alreadyImported = importIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Exercises
                .IgnoreQueryFilters()
                .Where(exercise =>
                    exercise.ClientEntityId != null
                    && importIds.Contains(exercise.ClientEntityId.Value))
                .Select(exercise => exercise.ClientEntityId!.Value)
                .ToListAsync(cancellationToken))
              .ToHashSet();
        _dedupMs += swDedup.ElapsedMilliseconds;

        var entities = new List<Exercise>(candidates.Count);
        var rowByEntity = new Dictionary<Exercise, CsvImportRow>(candidates.Count);
        foreach (var (row, importId) in candidates)
        {
            if (alreadyImported.Contains(importId))
            {
                job.AddSkipped(1);
                continue;
            }

            var (typeId, weightType) = typeByName[row.TypeName];
            var exercise = new Exercise
            {
                Id = Guid.CreateVersion7(),
                ClientEntityId = importId,
                Series = row.Series,
                Repetitions = row.Reps,
                Weight = row.Weight,
                Comments = row.Comments,
                TypeId = typeId,
                WorkoutId = workoutIdByDate[DateOnly.FromDateTime(row.Date)],
            };
            ComputeComputedValues(exercise, weightType, runningMaxByType);

            entities.Add(exercise);
            rowByEntity[exercise] = row;
        }

        if (entities.Count == 0)
        {
            return;
        }

        try
        {
            var swIns = System.Diagnostics.Stopwatch.StartNew();
            await InsertBatchAsync(entities, cancellationToken);
            _insertMs += swIns.ElapsedMilliseconds;
            job.AddProcessed(entities.Count);
        }
        catch (DbUpdateException batchEx)
        {
            // A batch failure must not lose the whole batch: retry row by row
            // so only the genuinely bad rows land in job.Errors.
            _logger.LogWarning(batchEx, "CSV import batch insert failed, retrying per row");
            _db.ChangeTracker.Clear();
            foreach (var exercise in entities)
            {
                try
                {
                    await InsertBatchAsync([exercise], cancellationToken);
                    job.AddProcessed(1);
                }
                catch (Exception rowEx)
                {
                    _db.ChangeTracker.Clear();
                    var row = rowByEntity[exercise];
                    // Full exception stays in the log; the job error carries the
                    // mapped, user-safe reason (t_a4651a49).
                    _logger.LogError(
                        rowEx,
                        "CSV import row {RowNumber} failed for user {UserId} (job {JobId})",
                        row.RowNumber, _userId, job.Id);
                    job.AddError(
                        row.RowNumber,
                        CsvImportErrorMapper.DescribeRowFailure(rowEx, row.RowNumber));
                }
            }
        }
    }

    private async Task InsertBatchAsync(
        List<Exercise> entities,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            _db.Exercises.AddRange(entities);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Raw multi-row INSERT: Pomelo's SaveChanges still sends one round trip
        // per entity (probe on the real schema: 10k rows = 6.9 s EF-tx vs
        // 4.3 s raw-tx). Multi-row statements keep it to one round trip per
        // batch inside the job transaction.
        // Same serializer as MainDbContext's Tags ValueConverter (private
        // there); import rows never carry tags, but serialize the entity's
        // value anyway so the raw path can't drift from the EF path.
        var tagsJsonByEntity = entities
            .Select(e => JsonSerializer.Serialize(e.Tags, (JsonSerializerOptions)null))
            .ToList();

        var values = new List<string>(entities.Count);
        var parameters = new List<MySqlParameter>(entities.Count * 14);
        for (var i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            values.Add(
                $"(@p{i}_id,@p{i}_ceid,@p{i}_series,@p{i}_reps,@p{i}_weight,@p{i}_maxrep,"
                + $"@p{i}_vol,@p{i}_int,@p{i}_com,@p{i}_type,@p{i}_workout,@p{i}_tags,"
                + $"@p{i}_bw,@p{i}_aw)");
            parameters.Add(new MySqlParameter($"@p{i}_id", e.Id));
            parameters.Add(new MySqlParameter($"@p{i}_ceid", Nullable(e.ClientEntityId)));
            parameters.Add(new MySqlParameter($"@p{i}_series", Nullable(e.Series)));
            parameters.Add(new MySqlParameter($"@p{i}_reps", Nullable(e.Repetitions)));
            parameters.Add(new MySqlParameter($"@p{i}_weight", Nullable(e.Weight)));
            parameters.Add(new MySqlParameter($"@p{i}_maxrep", Nullable(e.MaxRepetition)));
            parameters.Add(new MySqlParameter($"@p{i}_vol", Nullable(e.Volume)));
            parameters.Add(new MySqlParameter($"@p{i}_int", Nullable(e.Intensity)));
            parameters.Add(new MySqlParameter($"@p{i}_com", e.Comments ?? (object)DBNull.Value));
            parameters.Add(new MySqlParameter($"@p{i}_type", Nullable(e.TypeId)));
            parameters.Add(new MySqlParameter($"@p{i}_workout", Nullable(e.WorkoutId)));
            parameters.Add(new MySqlParameter($"@p{i}_tags", tagsJsonByEntity[i]));
            parameters.Add(new MySqlParameter($"@p{i}_bw", Nullable(e.BodyWeight)));
            parameters.Add(new MySqlParameter($"@p{i}_aw", Nullable(e.AddedWeight)));
        }

        var sql =
            "INSERT INTO Exercises (Id, ClientEntityId, Series, Repetitions, Weight, MaxRepetition, "
            + "Volume, Intensity, Comments, TypeId, WorkoutId, Tags, BodyWeight, AddedWeight) VALUES "
            + string.Join(",", values);

        try
        {
            await _db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
        catch (MySqlException ex)
        {
            // Surface as DbUpdateException so ProcessBatchAsync's per-row
            // fallback (which uses the EF path) handles the bad rows.
            throw new DbUpdateException("Import batch insert failed", ex);
        }
    }

    private static object Nullable<T>(T? value) where T : struct =>
        value.HasValue ? value.Value : (object)DBNull.Value;

    /// <summary>
    /// Mirrors ExercisesRepository.CalculateComputedValues, but with the
    /// WeightType preloaded and the per-type historical 1RM kept in a running
    /// dictionary instead of re-queried per row. Rows are processed in file
    /// order, so each row sees the max of all previous rows of its type —
    /// identical to the old sequential semantics.
    /// </summary>
    private static void ComputeComputedValues(
        Exercise exercise,
        string? weightType,
        Dictionary<int, double> runningMaxByType)
    {
        if (string.Equals(weightType, "None", StringComparison.OrdinalIgnoreCase))
        {
            exercise.Weight = 0;
            exercise.BodyWeight = null;
            exercise.AddedWeight = null;
            exercise.MaxRepetition = null;
            exercise.Volume = null;
            exercise.Intensity = null;
            return;
        }

        var weight = Math.Max(0, exercise.Weight ?? 0);
        var repetitions = Math.Max(0, exercise.Repetitions ?? 0);
        var series = Math.Max(0, exercise.Series ?? 0);
        exercise.Weight = weight;

        // Empty placeholder row (no reps, no load): no 1RM, no volume
        // (same rule as t_0f19c744).
        if (repetitions <= 0 && weight <= 0)
        {
            exercise.MaxRepetition = null;
            exercise.Volume = null;
            exercise.Intensity = null;
            return;
        }

        exercise.MaxRepetition = Math.Round(
            weight * (1 + 0.0333 * repetitions),
            1);
        exercise.Volume = (int)(repetitions * series * weight);

        var historicMaxRepetition = exercise.TypeId.HasValue
            && runningMaxByType.TryGetValue(exercise.TypeId.Value, out var runningMax)
            ? runningMax
            : (double?)null;
        var referenceMax = Math.Max(
            exercise.MaxRepetition ?? 0,
            historicMaxRepetition ?? 0);

        if (weight > 0 && referenceMax > 0)
        {
            exercise.Intensity = (int)Math.Round(weight / referenceMax * 100);
        }
        else
        {
            exercise.Intensity = null;
        }

        if (exercise.TypeId.HasValue && exercise.MaxRepetition.HasValue)
        {
            runningMaxByType[exercise.TypeId.Value] = Math.Max(
                runningMaxByType.GetValueOrDefault(exercise.TypeId.Value, 0),
                exercise.MaxRepetition.Value);
        }
    }

    /// <summary>
    /// Deterministic synthetic ClientEntityId from row content: makes imports
    /// idempotent (reimport skips instead of duplicating) and lets a crashed
    /// job be rerun safely.
    /// </summary>
    private Guid ImportEntityId(CsvImportRow row)
    {
        var date = DateOnly.FromDateTime(row.Date);
        var source = Encoding.UTF8.GetBytes(string.Join('\n',
            _userId,
            date.ToString("yyyy-MM-dd"),
            row.TypeName,
            row.Series?.ToString() ?? "",
            row.Reps?.ToString() ?? "",
            row.Weight?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            row.Comments ?? ""));
        var hash = SHA256.HashData(source);

        // Deterministic, but with the row date in the leading bytes: Guid's
        // string form (what char(36) stores and indexes) then sorts by date,
        // so bulk inserts touch contiguous pages of the unique index instead
        // of scattering random UUIDs across it.
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes, date.DayNumber);
        hash.AsSpan(4, 12).CopyTo(bytes.AsSpan(4));
        return new Guid(bytes);
    }
}
