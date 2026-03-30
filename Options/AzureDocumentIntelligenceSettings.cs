#nullable enable

using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

public class AzureDocumentIntelligenceSettings
{
    [ConfigurationKeyName("AzureDocumentIntelligenceEndpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [ConfigurationKeyName("AzureDocumentIntelligenceApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigurationKeyName("AzureDocumentIntelligenceModelId")]
    public string ModelId { get; set; } = "prebuilt-layout";

    [ConfigurationKeyName("AzureDocumentIntelligencePages")]
    public string? Pages { get; set; }

    [ConfigurationKeyName("AzureDocumentIntelligenceChunkScanEnabled")]
    public bool ChunkScanEnabled { get; set; }

    [ConfigurationKeyName("AzureDocumentIntelligenceAutoTopPageCount")]
    public int AutoTopPageCount { get; set; } = 6;

    [ConfigurationKeyName("AzureDocumentIntelligenceAutoMinPageScore")]
    public int AutoMinPageScore { get; set; } = 3;

    [ConfigurationKeyName("AzureDocumentIntelligenceEnabled")]
    public bool Enabled { get; set; }
}