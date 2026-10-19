using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed class DocumentAuditService(ApplicationDbContext db) : IDocumentAuditService
{
    public async Task RecordAsync(DocumentAction action, ActivityOutcome outcome, int? actorId, int? documentId, string? titleSnapshot, string? detail, CancellationToken ct = default)
    {
        db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = documentId,
            DocumentTitle = Truncate(titleSnapshot ?? string.Empty, 255),
            ActorUserId = actorId,
            Action = action,
            Outcome = outcome,
            Detail = Truncate(detail, 500),
            TimestampUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
