#nullable enable

using healthspan_api.Options;
using Microsoft.Extensions.Options;

public class FileSystemBloodTestDocumentStorage
{
    private readonly FileSystemDocumentStorageSettings _settings;

    public FileSystemBloodTestDocumentStorage(IOptions<FileSystemDocumentStorageSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<StoredBloodTestDocument?> StoreAsync(string userId, IFormFile file, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        var rootPath = ResolveBasePath();
        Directory.CreateDirectory(rootPath);

        var sanitizedUserId = string.Concat(userId.Where(ch => char.IsLetterOrDigit(ch) || ch == '-'));
        var extension = Path.GetExtension(file.FileName);
        var relativePath = Path.Combine(sanitizedUserId, DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"), $"{Guid.NewGuid()}{extension}");
        var absolutePath = Path.Combine(rootPath, relativePath);

        var directoryPath = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var targetStream = File.Create(absolutePath);
        await using var sourceStream = file.OpenReadStream();
        await sourceStream.CopyToAsync(targetStream, cancellationToken);

        return new StoredBloodTestDocument
        {
            ContainerName = "filesystem",
            BlobName = relativePath.Replace('\\', '/'),
            BlobUri = absolutePath
        };
    }

    public async Task<byte[]?> ReadAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
    {
        var absolutePath = ResolveStoredPath(document);
        return absolutePath is not null && File.Exists(absolutePath)
            ? await File.ReadAllBytesAsync(absolutePath, cancellationToken)
            : null;
    }

    public Task DeleteAsync(
        StoredBloodTestDocument document,
        CancellationToken cancellationToken)
    {
        var absolutePath = ResolveStoredPath(document);
        if (absolutePath is null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private string? ResolveStoredPath(StoredBloodTestDocument document)
    {
        if (!string.Equals(
                document.ContainerName,
                "filesystem",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(document.BlobName))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(ResolveBasePath());
        var rootPrefix = rootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var relativePath = document.BlobName.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(
            Path.Combine(rootPath, relativePath));

        if (!absolutePath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Stored document path is outside the configured root.");
        }

        return absolutePath;
    }

    private string ResolveBasePath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BasePath))
        {
            return _settings.BasePath;
        }

        var homePath = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(homePath))
        {
            return Path.Combine(homePath, "data", "blood-test-results");
        }

        return Path.Combine(AppContext.BaseDirectory, "App_Data", "blood-test-results");
    }
}
