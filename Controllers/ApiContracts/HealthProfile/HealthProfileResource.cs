#nullable enable

using System.ComponentModel.DataAnnotations;

public sealed class HealthProfileResource
{
    public string? Name { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public BiologicalSex BiologicalSex { get; set; }
    public int? HeightCm { get; set; }
    public int? WeightKg { get; set; }
}

public sealed class UpdateHealthProfileResource
{
    [StringLength(100)]
    public string? Name { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public BiologicalSex BiologicalSex { get; set; }

    [Range(80, 250)]
    public int? HeightCm { get; set; }

    [Range(20, 500)]
    public int? WeightKg { get; set; }
}
