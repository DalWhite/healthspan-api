public sealed class HealthProfileResource
{
    public DateOnly? DateOfBirth { get; set; }
    public BiologicalSex BiologicalSex { get; set; }
}

public sealed class UpdateHealthProfileResource
{
    public DateOnly? DateOfBirth { get; set; }
    public BiologicalSex BiologicalSex { get; set; }
}
