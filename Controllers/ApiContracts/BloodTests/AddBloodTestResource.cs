#nullable enable

public class AddBloodTestResource
{
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public List<BloodTestMarkerResource> Markers { get; set; } = new();
}