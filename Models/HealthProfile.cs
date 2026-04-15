#nullable enable

public class HealthProfile
{
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateOnly? DateOfBirth { get; set; }
    public BiologicalSex BiologicalSex { get; set; } = BiologicalSex.Unknown;
}

public enum BiologicalSex
{
    Unknown,
    Female,
    Male
}