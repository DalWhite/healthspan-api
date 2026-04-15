using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

public class FileSystemDocumentStorageSettings
{
    [ConfigurationKeyName("FileSystemDocumentStorageEnabled")]
    public bool Enabled { get; set; }

    [ConfigurationKeyName("FileSystemDocumentStorageBasePath")]
    public string BasePath { get; set; } = string.Empty;
}