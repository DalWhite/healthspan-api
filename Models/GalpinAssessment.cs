#nullable enable

public class GalpinAssessment
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime AssessedOnUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<GalpinAssessmentScore> Scores { get; set; } = new();
}

public class GalpinAssessmentScore
{
    public Guid Id { get; set; }
    public Guid GalpinAssessmentId { get; set; }
    public GalpinAssessment GalpinAssessment { get; set; } = null!;
    public GalpinAdaptationType Adaptation { get; set; }
    public decimal? Score { get; set; }
    public string? EvidenceNotes { get; set; }
}

public enum GalpinAdaptationType
{
    Skill,
    Speed,
    Power,
    Strength,
    Hypertrophy,
    MuscularEndurance,
    AnaerobicCapacity,
    MaximalAerobicPower,
    LongDurationEndurance
}