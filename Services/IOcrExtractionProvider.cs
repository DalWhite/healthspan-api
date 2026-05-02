#nullable enable

public interface IOcrExtractionProvider
{
    Task<OcrExtractionResult> ExtractAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken);
}

public class OcrExtractionResult
{
    public bool Success { get; set; }
    public string? RawPayload { get; set; }
    public string? ErrorMessage { get; set; }
    public List<OcrMarkerResult> Markers { get; set; } = new();
}

public class OcrMarkerResult
{
    public string Name { get; set; } = string.Empty;
    public string? OriginalName { get; set; }
    public string? Unit { get; set; }
    public string? ValueText { get; set; }
    public decimal? Value { get; set; }
    public double? Confidence { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}