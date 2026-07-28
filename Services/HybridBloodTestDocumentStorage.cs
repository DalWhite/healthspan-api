#nullable enable

public class HybridBloodTestDocumentStorage : IBloodTestDocumentStorage
{
    private readonly AzureBlobBloodTestDocumentStorage _blobStorage;
    private readonly FileSystemBloodTestDocumentStorage _fileSystemStorage;

    public HybridBloodTestDocumentStorage(AzureBlobBloodTestDocumentStorage blobStorage, FileSystemBloodTestDocumentStorage fileSystemStorage)
    {
        _blobStorage = blobStorage;
        _fileSystemStorage = fileSystemStorage;
    }

    public async Task<StoredBloodTestDocument?> StoreAsync(string userId, IFormFile file, CancellationToken cancellationToken)
    {
        var storedInBlob = await _blobStorage.StoreAsync(userId, file, cancellationToken);
        if (storedInBlob is not null)
        {
            return storedInBlob;
        }

        return await _fileSystemStorage.StoreAsync(userId, file, cancellationToken);
    }

    public Task<byte[]?> ReadAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
        => string.Equals(
            document.ContainerName,
            "filesystem",
            StringComparison.OrdinalIgnoreCase)
            ? _fileSystemStorage.ReadAsync(document, cancellationToken)
            : _blobStorage.ReadAsync(document, cancellationToken);

    public Task DeleteAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
        => string.Equals(
            document.ContainerName,
            "filesystem",
            StringComparison.OrdinalIgnoreCase)
            ? _fileSystemStorage.DeleteAsync(document, cancellationToken)
            : _blobStorage.DeleteAsync(document, cancellationToken);
}
