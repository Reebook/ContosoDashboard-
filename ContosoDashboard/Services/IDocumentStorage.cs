namespace ContosoDashboard.Services;

public interface IDocumentStorage
{
    Task PutAsync(StorageArea area, string blobName, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string blobName, CancellationToken ct);
    Task PromoteAsync(string blobName, CancellationToken ct);
    Task DeleteAsync(string blobName, CancellationToken ct);
}
