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
}