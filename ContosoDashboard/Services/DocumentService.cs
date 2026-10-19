using System.Security.Cryptography;
using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public sealed class DocumentService : IDocumentService
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private readonly ApplicationDbContext _db;
    private readonly IDocumentStorage _storage;
    private readonly IMalwareScanner _scanner;
    private readonly IDocumentAuthorizationService _authorization;
    private readonly IDocumentAuditService _audit;
    private readonly DocumentOptions _options;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(ApplicationDbContext db, IDocumentStorage storage, IMalwareScanner scanner,
        IDocumentAuthorizationService authorization, IDocumentAuditService audit,
        IOptions<DocumentOptions> options, ILogger<DocumentService> logger)
    {
        _db = db;
        _storage = storage;
        _scanner = scanner;
        _authorization = authorization;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(int actorId, UploadRequest request, Stream content, CancellationToken ct)
    {
        string? title = request?.Title;
        try
        {
            if (request is null) throw new DocumentOperationException(400, "Upload metadata is required.");
            if (actorId <= 0) throw new DocumentOperationException(403, "A valid signed-in user is required.");
            var fileName = NormalizeFileName(request.FileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || !ContentTypes.TryGetValue(extension, out var expectedType))
                throw new DocumentOperationException(415, "This file type is not supported.");
            if (!string.Equals(NormalizeContentType(request.ContentType), expectedType, StringComparison.OrdinalIgnoreCase))
                throw new DocumentOperationException(415, "The file extension and content type do not agree.");
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 255)
                throw new DocumentOperationException(400, "A title of 1 to 255 characters is required.");
            if (!Enum.IsDefined(request.Category)) throw new DocumentOperationException(400, "Choose a valid document category.");
            if (request.Tags?.Length > 500) throw new DocumentOperationException(400, "Tags cannot exceed 500 characters.");
            if (request.Description?.Length > 2000) throw new DocumentOperationException(400, "Description cannot exceed 2000 characters.");
            if (request.UploadToken == Guid.Empty) throw new DocumentOperationException(400, "A valid upload token is required.");

            var projectId = request.ProjectId;
            if (request.TaskId.HasValue)
            {
                var task = await _db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.TaskId == request.TaskId.Value, ct);
                if (task is null) throw new DocumentOperationException(400, "The selected task does not exist.");
                if (projectId.HasValue && task.ProjectId != projectId)
                    throw new DocumentOperationException(400, "The selected task does not belong to the selected project.");
                projectId ??= task.ProjectId;
            }
            if (projectId.HasValue && !await _authorization.CanUploadToProjectAsync(actorId, projectId.Value, ct))
                throw new DocumentOperationException(403, "You are not assigned to that project.");

            await using var buffer = new MemoryStream();
            var copyBuffer = new byte[81920];
            int count;
            while ((count = await content.ReadAsync(copyBuffer.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + count > _options.MaxFileSizeBytes)
                    throw new DocumentOperationException(413, "The file exceeds the 25 MB limit.");
                await buffer.WriteAsync(copyBuffer.AsMemory(0, count), ct);
            }
            if (buffer.Length == 0) throw new DocumentOperationException(400, "The selected file is empty.");
            var bytes = buffer.ToArray();
            if (!HasExpectedSignature(extension, bytes))
                throw new DocumentOperationException(415, "The file content does not match its extension.");
            var hash = ComputeUploadHash(request, fileName, projectId, bytes);

            var existing = await _db.Documents.SingleOrDefaultAsync(d => d.UploaderId == actorId && d.UploadToken == request.UploadToken, ct);
            if (existing is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.UploadHash), Convert.FromHexString(hash)))
                    throw new DocumentOperationException(409, "This upload token was already used for different content.");
                return new UploadResult(existing.DocumentId, existing.ScanStatus, true, "This upload was already received.");
            }

            var document = new Document
            {
                Title = request.Title.Trim(),
                Description = NullIfEmpty(request.Description),
                Category = request.Category,
                Tags = NormalizeTags(request.Tags),
                ProjectId = projectId,
                UploaderId = actorId,
                OriginalFileName = fileName,
                ContentType = expectedType,
                FileSize = bytes.LongLength,
                BlobName = "pending/" + Guid.NewGuid().ToString("N"),
                ScanStatus = ScanStatus.Pending,
                UploadDateUtc = DateTime.UtcNow,
                UpdatedDateUtc = DateTime.UtcNow,
                UploadToken = request.UploadToken,
                UploadHash = hash
            };

            var blobWritten = false;
            var blobName = string.Empty;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.Documents.Add(document);
                await _db.SaveChangesAsync(ct); // reserve the generated database id inside an uncommitted transaction
                blobName = $"{document.DocumentId}/{Guid.NewGuid():N}";
                document.BlobName = blobName;
                await using var uploadStream = new MemoryStream(bytes, writable: false);
                await _storage.PutAsync(StorageArea.Quarantine, blobName, uploadStream, expectedType, ct);
                blobWritten = true;
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (blobWritten || !string.IsNullOrEmpty(blobName))
                {
                    try { await _storage.DeleteAsync(blobName, CancellationToken.None); }
                    catch (Exception cleanupError) { _logger.LogError(cleanupError, "Failed to clean up an uncommitted document blob {BlobName}", blobName); }
                }
                throw;
            }

            await _audit.RecordAsync(DocumentAction.Upload, ActivityOutcome.Success, actorId, document.DocumentId, document.Title, "Upload received; scan pending.", ct);
            return new UploadResult(document.DocumentId, document.ScanStatus);
        }
        catch (DocumentOperationException ex)
        {
            try { await _audit.RecordAsync(DocumentAction.Upload, ActivityOutcome.Failure, actorId, null, title, ex.Message, ct); }
            catch (Exception auditError) { _logger.LogWarning(auditError, "Could not record failed upload attempt for user {ActorId}", actorId); }
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetMyDocumentsAsync(int actorId, CancellationToken ct = default)
    {
        return await _db.Documents.AsNoTracking().Where(d => d.UploaderId == actorId)
            .OrderByDescending(d => d.UploadDateUtc)
            .Select(d => new DocumentListItem(d.DocumentId, d.Title, d.Category, d.FileSize, d.UploadDateUtc, d.ScanStatus, d.OriginalFileName))
            .ToListAsync(ct);
    }

    public async Task<DocumentDownload?> OpenForDownloadAsync(int actorId, int documentId, bool inline, CancellationToken ct = default)
    {
        var document = await _db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.DocumentId == documentId, ct);
        if (document is null)
        {
            await _audit.RecordAsync(DocumentAction.Denied, ActivityOutcome.Denied, actorId, documentId, null, "Document missing or unavailable.", ct);
            return null;
        }
        var access = await _authorization.GetAccessAsync(actorId, documentId, ct);
        if (document.ScanStatus != ScanStatus.Clean || !access.CanDownload)
        {
            await _audit.RecordAsync(DocumentAction.Denied, ActivityOutcome.Denied, actorId, documentId, document.Title, "Document missing or unavailable.", ct);
            return null;
        }
        if (inline && document.ContentType is not ("application/pdf" or "image/jpeg" or "image/png"))
            throw new DocumentOperationException(415, "This document type cannot be previewed.");
        try
        {
            var stream = await _storage.OpenReadAsync(document.BlobName, ct);
            await _audit.RecordAsync(inline ? DocumentAction.Preview : DocumentAction.Download, ActivityOutcome.Success, actorId, documentId, document.Title, null, ct);
            return new DocumentDownload(stream, document.OriginalFileName, document.ContentType, document.FileSize);
        }
        catch (FileNotFoundException)
        {
            await _audit.RecordAsync(DocumentAction.Download, ActivityOutcome.Failure, actorId, documentId, document.Title, "Stored file was unavailable.", ct);
            return null;
        }
    }

    public async Task<IReadOnlyList<Project>> GetUploadProjectsAsync(int actorId, CancellationToken ct = default)
    {
        return await _db.Projects.AsNoTracking().Where(project => project.ProjectManagerId == actorId ||
            project.ProjectMembers.Any(member => member.UserId == actorId))
            .OrderBy(project => project.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TaskItem>> GetProjectTasksAsync(int actorId, int projectId, CancellationToken ct = default)
    {
        if (!await _authorization.CanUploadToProjectAsync(actorId, projectId, ct)) return [];
        return await _db.Tasks.AsNoTracking().Where(task => task.ProjectId == projectId)
            .OrderBy(task => task.Title).ToListAsync(ct);
    }

    private static string NormalizeFileName(string value)
    {
        var fileName = Path.GetFileName((value ?? string.Empty).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255) throw new DocumentOperationException(400, "The filename is invalid or too long.");
        return fileName;
    }

    private static string NormalizeContentType(string? value) => (value ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeTags(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(",", value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase));

    private static bool HasExpectedSignature(string extension, byte[] bytes)
    {
        static bool Starts(byte[] data, params byte[] signature) => data.Length >= signature.Length && data.AsSpan(0, signature.Length).SequenceEqual(signature);
        return extension switch
        {
            ".pdf" => Starts(bytes, 0x25, 0x50, 0x44, 0x46, 0x2D),
            ".doc" or ".xls" or ".ppt" => Starts(bytes, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1),
            ".docx" or ".xlsx" or ".pptx" => Starts(bytes, 0x50, 0x4B, 0x03, 0x04),
            ".jpg" or ".jpeg" => Starts(bytes, 0xFF, 0xD8, 0xFF),
            ".png" => Starts(bytes, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".txt" => IsText(bytes),
            _ => false
        };
    }

    private static bool IsText(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return !bytes.Any(b => b == 0 || (b < 0x09) || (b > 0x0D && b < 0x20));
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static string ComputeUploadHash(UploadRequest request, string fileName, int? projectId, byte[] bytes)
    {
        var metadata = Encoding.UTF8.GetBytes($"{request.Title.Trim()}\n{request.Category}\n{request.Description}\n{projectId}\n{request.TaskId}\n{request.Tags}\n{fileName}\n{request.ContentType}");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(metadata);
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
