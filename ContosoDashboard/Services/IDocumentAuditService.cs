using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentAuditService
{
    Task RecordAsync(DocumentAction action, ActivityOutcome outcome, int? actorId, int? documentId, string? titleSnapshot, string? detail, CancellationToken ct = default);
}
