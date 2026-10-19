using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<UploadResult> UploadAsync(int actorId, UploadRequest request, Stream content, CancellationToken ct);
    Task<IReadOnlyList<DocumentListItem>> GetMyDocumentsAsync(int actorId, CancellationToken ct = default);
    Task<DocumentDownload?> OpenForDownloadAsync(int actorId, int documentId, bool inline, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetUploadProjectsAsync(int actorId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetProjectTasksAsync(int actorId, int projectId, CancellationToken ct = default);
}
