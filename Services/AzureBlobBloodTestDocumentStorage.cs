#nullable enable

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using healthspan_api.Options;
using Microsoft.Extensions.Options;

public class AzureBlobBloodTestDocumentStorage
{
    private readonly AzureBlobStorageSettings _settings;

    public AzureBlobBloodTestDocumentStorage(IOptions<AzureBlobStorageSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<StoredBloodTestDocument?> StoreAsync(string userId, IFormFile file, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            return null;
        }

        var container = new BlobContainerClient(_settings.ConnectionString, _settings.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var extension = Path.GetExtension(file.FileName);
        var sanitizedUserId = string.Concat(userId.Where(ch => char.IsLetterOrDigit(ch) || ch == '-'));
        var blobName = $"{sanitizedUserId}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid()}{extension}";
        var blobClient = container.GetBlobClient(blobName);

        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
            },
            Metadata =
            {
                ["userId"] = sanitizedUserId,
                ["originalFileName"] = file.FileName
            }
        }, cancellationToken);

        return new StoredBloodTestDocument
        {
            ContainerName = container.Name,
            BlobName = blobName,
            BlobUri = blobClient.Uri.ToString()
        };
    }

    public async Task<byte[]?> ReadAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString)
            || string.IsNullOrWhiteSpace(document.ContainerName)
            || string.IsNullOrWhiteSpace(document.BlobName))
        {
            return null;
        }

        var blobClient = new BlobContainerClient(
                _settings.ConnectionString,
                document.ContainerName)
            .GetBlobClient(document.BlobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var content = await blobClient.DownloadContentAsync(
            cancellationToken);
        return content.Value.Content.ToArray();
    }

    public async Task DeleteAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString)
            || string.IsNullOrWhiteSpace(document.ContainerName)
            || string.IsNullOrWhiteSpace(document.BlobName))
        {
            return;
        }

        var container = new BlobContainerClient(
            _settings.ConnectionString,
            document.ContainerName);
        await container
            .GetBlobClient(document.BlobName)
            .DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
    }
}
