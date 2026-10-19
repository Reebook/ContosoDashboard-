using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentAuthorizationService(ApplicationDbContext db) : IDocumentAuthorizationService
{
    public async Task<DocumentAccess> GetAccessAsync(int actorId, int documentId, CancellationToken ct = default)
    {
        var document = await db.Documents.AsNoTracking().Include(d => d.Uploader).Include(d => d.Project)
            .SingleOrDefaultAsync(d => d.DocumentId == documentId, ct);
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == actorId, ct);
        if (document is null || actor is null) return new(false, false, false);

        var owner = document.UploaderId == actorId;
        var admin = actor.Role == UserRole.Administrator;
        var manager = document.Project?.ProjectManagerId == actorId;
        var departmentLead = actor.Role == UserRole.TeamLead &&
            string.Equals(actor.Department, document.Uploader.Department, StringComparison.OrdinalIgnoreCase);
        var member = document.ProjectId.HasValue && await db.ProjectMembers.AnyAsync(
            m => m.ProjectId == document.ProjectId && m.UserId == actorId, ct);
        var canManage = owner || admin || manager || departmentLead;
        var clean = document.ScanStatus == ScanStatus.Clean;
        var canView = clean && (canManage || member);
        return new DocumentAccess(canView, canView, canManage);
    }

    public async Task<bool> CanUploadToProjectAsync(int actorId, int projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (project is null) return false;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == actorId, ct);
        if (user is null) return false;
        if (user.Role == UserRole.Administrator || project.ProjectManagerId == actorId) return true;
        return await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == actorId, ct);
    }
}
