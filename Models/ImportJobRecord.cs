/// <summary>
/// Durable snapshot of one CSV import job (t_e26d8b7d). The in-memory
/// CsvImportJob stays the live source of truth while the API process owns
/// the job; this table is written on start, every few seconds while running
/// and on completion so GET /data/import/{jobId} survives an API restart
/// instead of returning 404 mid-import (QA finding 1 in t_79d39b1e).
/// </summary>
public class ImportJobRecord
{
    /// <summary>JobId (Guid "N" format) — same value POST /data/import returns.</summary>
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Status { get; set; } = "queued";
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Skipped { get; set; }
    public int ErrorCount { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    /// <summary>Last snapshot write; used to tell a live job from an orphan.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>JSON list of CsvImportRowError, capped at CsvImportJob.MaxStoredErrors.</summary>
    public string? ErrorsJson { get; set; }
}
