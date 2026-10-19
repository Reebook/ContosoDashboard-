using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentAuthorizationService
{
    Task<DocumentAccess> GetAccessAsync(int actorId, int documentId, CancellationToken ct = default);
    Task<bool> CanUploadToProjectAsync(int actorId, int projectId, CancellationToken ct = default);
}
