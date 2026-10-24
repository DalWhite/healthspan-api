using System.Collections.Concurrent;
using System.Text.Json;
using healthspan_api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory state of one CSV import job (t_c1f8e6ac). Thread-safe: written by
/// the background pipeline, read by GET /data/import/{jobId} polling.
/// Durable snapshots of this state go to ImportJobRecords (t_e26d8b7d) so the
/// job survives an API restart; after a restart the job is hydrated from the
/// store instead of returning 404.
/// </summary>
public sealed class CsvImportJob
{
    internal const int MaxStoredErrors = 1_000;

    private readonly object _gate = new();
    private readonly Queue<CsvImportRowError> _storedErrors = new();
    private int _errorCount;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string UserId { get; init; } = "";
    public int Total { get; init; }

    public int Processed;
    public int Skipped;
    public volatile string Status = "queued";
    public DateTimeOffset StartedAt;
    public DateTimeOffset? FinishedAt;
    public string FailureReason;

    public int ErrorCount => Volatile.Read(ref _errorCount);

    public void AddError(int row, string message)
    {
        Interlocked.Increment(ref _errorCount);
        lock (_gate)
        {
            if (_storedErrors.Count < MaxStoredErrors)
            {
                _storedErrors.Enqueue(new CsvImportRowError(row, message));
            }
        }
    }

    public void AddErrors(IEnumerable<CsvImportRowError> errors)
    {
        foreach (var error in errors)
        {
            AddError(error.Row, error.Message);
        }
    }

    public void AddProcessed(int count) => Interlocked.Add(ref Processed, count);

    public void AddSkipped(int count) => Interlocked.Add(ref Skipped, count);

    public object ToStatusDto(int maxErrorsExposed)
    {
        List<CsvImportRowError> errors;
        lock (_gate)
        {
            errors = _storedErrors.Take(maxErrorsExposed).ToList();
        }

        return new
        {
            jobId = Id,
            status = Status,
            total = Total,
            processed = Processed,
            skipped = Skipped,
            errors,
            errorCount = ErrorCount,
            startedAt = StartedAt,
            finishedAt = FinishedAt,
            failureReason = FailureReason,
        };
    }

    /// <summary>JSON snapshot of the stored errors, taken under the same gate
    /// the live DTO read uses. Null when there are none (keeps rows small).</summary>
    internal string? CaptureErrorsJson()
    {
        lock (_gate)
        {
            if (_storedErrors.Count == 0)
            {
                return null;
            }
            return JsonSerializer.Serialize(_storedErrors);
        }
    }

    /// <summary>Rebuild a job from its durable snapshot (t_e26d8b7d). The
    /// rebuilt job is read-only in practice: nothing re-runs it.</summary>
    internal static CsvImportJob Hydrate(ImportJobRecord record)
    {
        var job = new CsvImportJob
        {
            Id = record.Id,
            UserId = record.UserId,
            Total = record.Total,
        };
        job.Status = record.Status;
        job.Processed = record.Processed;
        job.Skipped = record.Skipped;
        job.StartedAt = record.StartedAt ?? default;
        job.FinishedAt = record.FinishedAt;
        job.FailureReason = record.FailureReason ?? "";

        if (!string.IsNullOrEmpty(record.ErrorsJson))
        {
            List<CsvImportRowError>? errors = null;
            try
            {
                errors = JsonSerializer.Deserialize<List<CsvImportRowError>>(record.ErrorsJson);
            }
            catch (JsonException)
            {
                // A corrupt snapshot must not break the status endpoint.
            }
            job.AddErrors(errors ?? new List<CsvImportRowError>());
        }

        // ErrorCount counts every error ever added (capped storage aside);
        // the snapshot stored the authoritative value.
        var missing = record.ErrorCount - job.ErrorCount;
        for (var i = 0; i < missing; i++)
        {
            Interlocked.Increment(ref job._errorCount);
        }

        return job;
    }
}

/// <summary>
/// Singleton registry that accepts CSV import jobs and runs them in the
/// background, so POST /data/import returns 202 immediately and the FE polls
/// progress instead of holding a 300 s request open (t_37acc65b contract).
///
/// Job state is snapshotted to ImportJobRecords (t_e26d8b7d): on start, every
/// SnapshotInterval while running, and on completion. GET hydrates from the
/// store when the job is not live in this process, so an API restart mid-import
/// returns 200 with the last known state instead of 404. A queued/running
/// record with no live owner in this process is an orphan from a restart and
/// is reported as failed with an interruption reason (the uploaded rows are
/// not persisted, so the import cannot resume; reimporting the same CSV is
/// idempotent via the deterministic ClientEntityId, so the user can just retry).
/// Single-instance assumption: a second API instance running the same job
/// would be mistaken for an orphan.
/// </summary>
public sealed class CsvImportJobService
{
    public const int MaxErrorsExposed = 100;

    private static readonly TimeSpan JobRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(2);
    private const int MaxFailureReasonLength = 2000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CsvImportJobService> _logger;
    private readonly ConcurrentDictionary<string, CsvImportJob> _jobs = new();

    // t_633ebd61: one running import per user within this app instance. A
    // second same-user submit is accepted as a "queued" job and runs after
    // the active one finishes, instead of racing it on
    // IX_Workouts_DailySessionKey and aborting as "failed". (Instance-scoped
    // on purpose: separate factories/processes use separate databases, and
    // cross-process races stay covered by the repository-level retry in
    // GetOrCreateWorkoutSessionsBulkAsync.)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userGates = new();

    public CsvImportJobService(
        IServiceScopeFactory scopeFactory,
        ILogger<CsvImportJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<CsvImportJob> StartAsync(
        string userId,
        List<CsvImportRow> rows,
        List<CsvImportRowError> parseErrors)
    {
        await PurgeExpiredJobsAsync();

        var job = new CsvImportJob { UserId = userId, Total = rows.Count };
        job.AddErrors(parseErrors);
        _jobs[job.Id] = job;

        // Persist before returning 202: the jobId must be findable even if
        // the process dies before the first snapshot tick.
        await PersistSnapshotAsync(job);

        _ = Task.Run(() => RunJobAsync(job, rows));
        return job;
    }

    public async Task<CsvImportJob?> FindAsync(string jobId, string userId)
    {
        if (_jobs.TryGetValue(jobId, out var live) && live.UserId == userId)
        {
            return live;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var record = await db.ImportJobRecords
                .FirstOrDefaultAsync(entry => entry.Id == jobId && entry.UserId == userId);
            if (record is null)
            {
                return null;
            }

            if (record.Status is "queued" or "running")
            {
                // Not live in this process => orphaned by an API restart.
                record.Status = "failed";
                record.FailureReason =
                    "La importación se interrumpió por un reinicio del servidor. "
                    + "Las filas ya importadas se conservaron; vuelve a subir el mismo CSV para continuar "
                    + "(las filas duplicadas se omiten).";
                record.FinishedAt ??= record.UpdatedAt;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            return CsvImportJob.Hydrate(record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hydrate CSV import job {JobId}", jobId);
            return null;
        }
    }

    private async Task RunJobAsync(CsvImportJob job, List<CsvImportRow> rows)
    {
        // t_633ebd61: same-user imports are serialized within the instance.
        // The second submit stays "queued" while the first runs, then runs
        // on the safe (idempotent-skip) path instead of racing the bulk
        // session INSERT on IX_Workouts_DailySessionKey.
        var gate = _userGates.GetOrAdd(job.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        job.StartedAt = DateTimeOffset.UtcNow;
        job.Status = "running";
        await PersistSnapshotAsync(job);

        using var snapshotCts = new CancellationTokenSource();
        var snapshotTask = Task.Run(() => SnapshotLoopAsync(job, snapshotCts.Token));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var workouts = scope.ServiceProvider.GetRequiredService<WorkoutsRepository>();

            var pipeline = new CsvImportPipeline(db, workouts, job.UserId);
            await pipeline.RunAsync(rows, job);

            job.Status = job.ErrorCount > 0 ? "completed_with_errors" : "completed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV import job {JobId} failed", job.Id);
            job.FailureReason = ex.Message;
            job.Status = "failed";
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            snapshotCts.Cancel();
            try
            {
                await snapshotTask;
            }
            catch (Exception)
            {
                // Snapshot loop failures are already logged inside.
            }
            await PersistSnapshotAsync(job);
            gate.Release();
        }
    }

    private async Task SnapshotLoopAsync(CsvImportJob job, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SnapshotInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (job.FinishedAt is not null)
            {
                break;
            }

            // A failed snapshot means the store is gone (e.g. host shutdown);
            // retrying would just log noise.
            if (!await PersistSnapshotAsync(job))
            {
                break;
            }
        }
    }

    /// <summary>Upsert the job's current state into ImportJobRecords.
    /// Returns false on failure (logged) — job state stays usable in memory.</summary>
    private async Task<bool> PersistSnapshotAsync(CsvImportJob job)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

            var record = await db.ImportJobRecords
                .FirstOrDefaultAsync(entry => entry.Id == job.Id);
            if (record is null)
            {
                record = new ImportJobRecord { Id = job.Id };
                db.ImportJobRecords.Add(record);
            }

            record.UserId = job.UserId;
            record.Status = job.Status;
            record.Total = job.Total;
            record.Processed = job.Processed;
            record.Skipped = job.Skipped;
            record.ErrorCount = job.ErrorCount;
            record.FailureReason = job.FailureReason is null
                ? null
                : (job.FailureReason.Length > MaxFailureReasonLength
                    ? job.FailureReason[..MaxFailureReasonLength]
                    : job.FailureReason);
            record.StartedAt = job.StartedAt == default ? null : job.StartedAt;
            record.FinishedAt = job.FinishedAt;
            record.ErrorsJson = job.CaptureErrorsJson();
            record.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist CSV import job {JobId}", job.Id);
            return false;
        }
    }

    private async Task PurgeExpiredJobsAsync()
    {
        var cutoff = DateTimeOffset.UtcNow - JobRetention;
        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedAt is { } finished && finished < cutoff)
            {
                _jobs.TryRemove(id, out _);
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var expired = await db.ImportJobRecords
                .Where(entry => entry.FinishedAt != null && entry.FinishedAt < cutoff)
                .Select(entry => entry.Id)
                .ToListAsync();
            if (expired.Count > 0)
            {
                db.ImportJobRecords.RemoveRange(
                    db.ImportJobRecords.Where(entry => expired.Contains(entry.Id)));
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not purge expired CSV import jobs");
        }
    }
}
