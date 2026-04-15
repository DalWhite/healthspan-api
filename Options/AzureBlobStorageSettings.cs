using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

public class AzureBlobStorageSettings
{
    [ConfigurationKeyName("AzureBlobStorageEnabled")]
    public bool Enabled { get; set; }

    [ConfigurationKeyName("AzureBlobStorageConnectionString")]
    public string ConnectionString { get; set; } = string.Empty;

    [ConfigurationKeyName("AzureBlobStorageContainerName")]
    public string ContainerName { get; set; } = "blood-test-results";
}