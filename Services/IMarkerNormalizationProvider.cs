#nullable enable

public interface IMarkerNormalizationProvider
{
    Task<MarkerNormalizationResult> NormalizeAsync(MarkerNormalizationRequest request, CancellationToken cancellationToken);
}

public class MarkerNormalizationRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? RawExtractionPayload { get; set; }
    public List<OcrMarkerResult> CandidateMarkers { get; set; } = new();
}

public class MarkerNormalizationResult
{
    public bool Success { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
    public long? LatencyMs { get; set; }
    public int? InputTokenEstimate { get; set; }
    public int? OutputTokenEstimate { get; set; }
    public List<OcrMarkerResult> Markers { get; set; } = new();
}

public class NoOpMarkerNormalizationProvider : IMarkerNormalizationProvider
{
    public Task<MarkerNormalizationResult> NormalizeAsync(MarkerNormalizationRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MarkerNormalizationResult
        {
            Success = false,
            ProviderName = "disabled",
            ErrorMessage = "LLM marker normalization is disabled."
        });
    }
}