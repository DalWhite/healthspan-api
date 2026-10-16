/// <summary>
/// Audit/dedup log of sync operations already applied by POST /sync.
/// The client replays its queue until the server confirms, so the OpId
/// unique index is what makes sync idempotent end to end.
/// </summary>
public class SyncOperation
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid OpId { get; set; }
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? ClientEntityId { get; set; }
    public Guid? ServerEntityId { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
