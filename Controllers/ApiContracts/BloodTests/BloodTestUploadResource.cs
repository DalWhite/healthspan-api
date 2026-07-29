#nullable enable

public sealed class BloodTestUploadResource
{
    public Guid Id { get; set; }
    public BloodTestProcessingStatus ProcessingStatus { get; set; }
    public Guid? PossibleDuplicateBloodTestId { get; set; }
}
