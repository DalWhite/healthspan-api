using System.Collections.Concurrent;
using healthspan_api.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory state of one CSV import job (t_c1f8e6ac). Thread-safe: written by
/// the background pipeline, read by GET /data/import/{jobId} polling.
/// </summary>
public sealed class CsvImportJob
{
    private const int MaxStoredErrors = 1_000;

    private readonly object _gate = new();
    private readonly Queue<CsvImportRowError> _storedErrors = new();
    private int _errorCount;

    public string Id { get; } = Guid.NewGuid().ToString("N");
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
}

/// <summary>
/// Singleton registry that accepts CSV import jobs and runs them in the
/// background, so POST /data/import returns 202 immediately and the FE polls
/// progress instead of holding a 300 s request open (t_37acc65b contract).
/// </summary>
public sealed class CsvImportJobService
{
    public const int MaxErrorsExposed = 100;

    private static readonly TimeSpan JobRetention = TimeSpan.FromHours(24);

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

    public CsvImportJob Start(
        string userId,
        List<CsvImportRow> rows,
        List<CsvImportRowError> parseErrors)
    {
        PurgeExpiredJobs();

        var job = new CsvImportJob { UserId = userId, Total = rows.Count };
        job.AddErrors(parseErrors);
        _jobs[job.Id] = job;

        _ = Task.Run(() => RunJobAsync(job, rows));
        return job;
    }

    public CsvImportJob? Find(string jobId, string userId) =>
        _jobs.TryGetValue(jobId, out var job) && job.UserId == userId
            ? job
            : null;

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
            gate.Release();
        }
    }

    private void PurgeExpiredJobs()
    {
        var cutoff = DateTimeOffset.UtcNow - JobRetention;
        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedAt is { } finished && finished < cutoff)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }
}
