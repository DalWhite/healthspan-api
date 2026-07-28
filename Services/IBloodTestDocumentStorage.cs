#nullable enable

public interface IBloodTestDocumentStorage
{
    Task<StoredBloodTestDocument?> StoreAsync(string userId, IFormFile file, CancellationToken cancellationToken);
    Task<byte[]?> ReadAsync(StoredBloodTestDocument document, CancellationToken cancellationToken);
    Task DeleteAsync(StoredBloodTestDocument document, CancellationToken cancellationToken);
}

public class StoredBloodTestDocument
{
    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string BlobUri { get; set; } = string.Empty;
}
