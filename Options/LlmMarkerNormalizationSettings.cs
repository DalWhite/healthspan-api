#nullable enable

using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

public class LlmMarkerNormalizationSettings
{
    [ConfigurationKeyName("LlmMarkerNormalizationEnabled")]
    public bool Enabled { get; set; }

    [ConfigurationKeyName("LlmMarkerNormalizationProvider")]
    public string Provider { get; set; } = "OpenRouter";

    [ConfigurationKeyName("LlmMarkerNormalizationApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigurationKeyName("LlmMarkerNormalizationModel")]
    public string Model { get; set; } = string.Empty;

    [ConfigurationKeyName("LlmMarkerNormalizationBaseUrl")]
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

    [ConfigurationKeyName("LlmMarkerNormalizationPromptVersion")]
    public string PromptVersion { get; set; } = "v1";

    [ConfigurationKeyName("LlmMarkerNormalizationSiteUrl")]
    public string? SiteUrl { get; set; }

    [ConfigurationKeyName("LlmMarkerNormalizationAppName")]
    public string? AppName { get; set; } = "healthspan-api";

    [ConfigurationKeyName("LlmMarkerNormalizationMaxOcrChars")]
    public int MaxOcrChars { get; set; } = 6000;

    [ConfigurationKeyName("LlmMarkerNormalizationMaxCandidateMarkers")]
    public int MaxCandidateMarkers { get; set; } = 80;

    [ConfigurationKeyName("LlmMarkerNormalizationChunkSize")]
    public int ChunkSize { get; set; } = 10;

    [ConfigurationKeyName("LlmMarkerNormalizationTemperature")]
    public decimal Temperature { get; set; } = 0.1m;

    [ConfigurationKeyName("LlmMarkerNormalizationMaxOutputTokens")]
    public int MaxOutputTokens { get; set; } = 1200;

    [ConfigurationKeyName("LlmMarkerNormalizationTimeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    [ConfigurationKeyName("LlmMarkerNormalizationLogPayloads")]
    public bool LogPayloads { get; set; }

    [ConfigurationKeyName("LlmMarkerNormalizationLogPayloadMaxChars")]
    public int LogPayloadMaxChars { get; set; } = 20000;
}