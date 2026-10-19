using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

/// <summary>Private Azure Blob adapter with a filesystem-only Development fallback.</summary>
public sealed class BlobDocumentStorage : IDocumentStorage
{
    private readonly DocumentStorageOptions _options;
    private readonly string _localRoot;
    private readonly BlobServiceClient? _blobService;

    public BlobDocumentStorage(IOptions<DocumentOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value.Storage;
        _localRoot = Path.IsPathRooted(_options.LocalPath)
            ? _options.LocalPath
            : Path.Combine(environment.ContentRootPath, _options.LocalPath);
        if (!IsLocal)
        {
            if (Uri.TryCreate(_options.AccountUri, UriKind.Absolute, out var accountUri))
                _blobService = new BlobServiceClient(accountUri, new DefaultAzureCredential());
        }
    }

    private bool IsLocal => string.Equals(_options.Provider, "Local", StringComparison.OrdinalIgnoreCase);

    public async Task PutAsync(StorageArea area, string blobName, Stream content, string contentType, CancellationToken ct)
    {
        ValidateBlobName(blobName);
        if (IsLocal)
        {
            var path = GetLocalPath(area, blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(output, ct);
            return;
        }

        var container = await GetContainerAsync(area, ct);
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string blobName, CancellationToken ct)
    {
        ValidateBlobName(blobName);
        if (IsLocal)
        {
            var path = GetLocalPath(StorageArea.Clean, blobName);
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        var container = await GetContainerAsync(StorageArea.Clean, ct);
        return await container.GetBlobClient(blobName).OpenReadAsync(cancellationToken: ct);
    }

    public async Task PromoteAsync(string blobName, CancellationToken ct)
    {
        ValidateBlobName(blobName);
        if (IsLocal)
        {
            var source = GetLocalPath(StorageArea.Quarantine, blobName);
            var target = GetLocalPath(StorageArea.Clean, blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, overwrite: true);
            return;
        }

        var quarantine = await GetContainerAsync(StorageArea.Quarantine, ct);
        var clean = await GetContainerAsync(StorageArea.Clean, ct);
        var sourceBlob = quarantine.GetBlobClient(blobName);
        var targetBlob = clean.GetBlobClient(blobName);
        var copy = await targetBlob.StartCopyFromUriAsync(sourceBlob.Uri, cancellationToken: ct);
        await copy.WaitForCompletionAsync(ct);
        await sourceBlob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct)
    {
        ValidateBlobName(blobName);
        if (IsLocal)
        {
            foreach (var area in Enum.GetValues<StorageArea>())
            {
                var path = GetLocalPath(area, blobName);
                if (File.Exists(path)) File.Delete(path);
            }
            return;
        }
        foreach (var area in Enum.GetValues<StorageArea>())
        {
            var container = await GetContainerAsync(area, ct);
            await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
        }
    }

    private async Task<BlobContainerClient> GetContainerAsync(StorageArea area, CancellationToken ct)
    {
        if (_blobService is null)
            throw new InvalidOperationException("Azure document storage is not configured. Set Documents:Storage:AccountUri or use the Development local provider.");
        var name = area == StorageArea.Quarantine ? _options.QuarantineContainer : _options.CleanContainer;
        var container = _blobService.GetBlobContainerClient(name);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return container;
    }

    private string GetLocalPath(StorageArea area, string blobName)
    {
        var areaName = area == StorageArea.Quarantine ? "quarantine" : "documents";
        var path = Path.GetFullPath(Path.Combine(_localRoot, areaName, blobName.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(Path.Combine(_localRoot, areaName)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid document storage key.");
        return path;
    }

    private static void ValidateBlobName(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.StartsWith('/') || blobName.Contains("..", StringComparison.Ordinal) || blobName.Contains('\\'))
            throw new ArgumentException("Invalid generated document key.", nameof(blobName));
    }
}
