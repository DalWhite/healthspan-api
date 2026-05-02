#nullable enable

public class AnalyticsPermission
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public User OwnerUser { get; set; } = null!;
    public string ViewerUserId { get; set; } = string.Empty;
    public User ViewerUser { get; set; } = null!;
    public string? ParameterSubsetJson { get; set; }
    public bool CanViewBloodAnalytics { get; set; }
    public bool CanViewAssessments { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}