# Contract: Internal Service Interfaces (C#)

Namespace `ContosoDashboard.Services`. Signatures are the contract; bodies are for implementation. All methods take the acting `userId` and re-authorize.

```csharp
public interface IDocumentService
{
    Task<UploadResult> UploadAsync(int actorId, UploadRequest req, Stream content, CancellationToken ct);
    Task<ReplaceResult> ReplaceFileAsync(int actorId, int documentId, string fileName, string contentType, Stream content, CancellationToken ct);
    Task<PagedResult<DocumentListItem>> GetMyDocumentsAsync(int actorId, DocumentQuery q);      // sort: title|date|category|size; filter: category|project|date range
    Task<PagedResult<DocumentListItem>> GetProjectDocumentsAsync(int actorId, int projectId, DocumentQuery q);
    Task<PagedResult<DocumentListItem>> GetSharedWithMeAsync(int actorId, DocumentQuery q);
    Task<PagedResult<DocumentListItem>> SearchAsync(int actorId, string text, DocumentQuery q);  // auth predicate applied in query
    Task<DocumentDetails?> GetAsync(int actorId, int documentId);                                  // null = missing OR unauthorized
    Task<bool> UpdateMetadataAsync(int actorId, int documentId, DocumentMetadata m);
    Task<bool> DeleteAsync(int actorId, int documentId);                                           // hard delete + audit
    Task<DocumentStream?> OpenForDownloadAsync(int actorId, int documentId, bool inline);          // audits
    Task<List<DocumentListItem>> GetRecentAsync(int actorId, int count = 5);
    Task<int> GetCountAsync(int actorId);
    Task<bool> AttachToTaskAsync(int actorId, int taskId, int documentId);
    Task<List<DocumentListItem>> GetTaskDocumentsAsync(int actorId, int taskId);
}

public interface IDocumentShareService
{
    Task<bool> ShareWithUserAsync(int actorId, int documentId, int targetUserId);
    Task<bool> ShareWithTeamAsync(int actorId, int documentId, string team);
    Task<bool> RevokeAsync(int actorId, int shareId);
    Task<List<ShareInfo>> GetSharesAsync(int actorId, int documentId);
}

public interface IDocumentAuthorizationService
{
    Task<DocumentAccess> GetAccessAsync(int actorId, int documentId);   // flags: CanView, CanDownload, CanManage
    IQueryable<Document> ApplyReadScope(IQueryable<Document> q, ActorContext actor);
}

public interface IDocumentStorage            // swappable backend (Azure Blob default)
{
    Task PutAsync(StorageArea area, string blobName, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string blobName, CancellationToken ct);
    Task PromoteAsync(string blobName, CancellationToken ct);           // quarantine -> documents
    Task DeleteAsync(string blobName, CancellationToken ct);
}

public interface IMalwareScanner
{
    Task<ScanOutcome> GetResultAsync(string quarantineBlobName, CancellationToken ct); // Clean | Infected | Unavailable | Indeterminate
}

public interface IDocumentAuditService
{
    Task RecordAsync(DocumentAction action, ActivityOutcome outcome, int? actorId, int? documentId, string? titleSnapshot, string? detail);
    Task<UploadsByTypeReport> UploadsByTypeAsync(DateTime from, DateTime to);
    Task<List<TopUploader>> TopUploadersAsync(DateTime from, DateTime to);
    Task<AccessPatternReport> AccessPatternsAsync(DateTime from, DateTime to);
}
```

Behavior contracts:
- `Unavailable`/`Indeterminate` ⇒ document stays `Pending`, worker reschedules; never exposes content.
- Upload order: validate → `PutAsync(quarantine)` → DB insert; DB failure ⇒ `DeleteAsync`.
- `GetAsync`/`OpenForDownloadAsync` return `null` for unauthorized and nonexistent alike.
- `DashboardSummary` extended with `DocumentCount`; `INotificationService.CreateNotificationAsync` used for `DocumentShared`/`DocumentAdded`.
