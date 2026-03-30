#nullable enable

using healthspan_api.Options;
using Microsoft.Extensions.Options;

public class ConfiguredMarkerNormalizationProvider : IMarkerNormalizationProvider
{
    private readonly LlmMarkerNormalizationSettings _settings;
    private readonly OpenRouterMarkerNormalizationProvider _openRouterProvider;
    private readonly NoOpMarkerNormalizationProvider _noOpProvider;

    public ConfiguredMarkerNormalizationProvider(IOptions<LlmMarkerNormalizationSettings> settings, OpenRouterMarkerNormalizationProvider openRouterProvider, NoOpMarkerNormalizationProvider noOpProvider)
    {
        _settings = settings.Value;
        _openRouterProvider = openRouterProvider;
        _noOpProvider = noOpProvider;
    }

    public Task<MarkerNormalizationResult> NormalizeAsync(MarkerNormalizationRequest request, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return _noOpProvider.NormalizeAsync(request, cancellationToken);
        }

        return _settings.Provider.Trim().ToLowerInvariant() switch
        {
            "openrouter" => _openRouterProvider.NormalizeAsync(request, cancellationToken),
            _ => _noOpProvider.NormalizeAsync(request, cancellationToken)
        };
    }
}