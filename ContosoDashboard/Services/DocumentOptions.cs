namespace ContosoDashboard.Services;

public sealed class DocumentOptions
{
    public const long DefaultMaxFileSizeBytes = 25L * 1024 * 1024;
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png"];
    public int? AuditRetentionDays { get; set; }
    public DocumentStorageOptions Storage { get; set; } = new();
    public DocumentScannerOptions Scanner { get; set; } = new();
}

public sealed class DocumentStorageOptions
{
    public string Provider { get; set; } = "Azure";
    public string? AccountUri { get; set; }
    public string QuarantineContainer { get; set; } = "document-quarantine";
    public string CleanContainer { get; set; } = "documents";
    public string LocalPath { get; set; } = "App_Data/documents";
}

public sealed class DocumentScannerOptions
{
    public string Provider { get; set; } = "Defender";
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxPendingAgeHours { get; set; } = 24;
}

public enum StorageArea { Quarantine, Clean }
public enum ScanOutcome { Clean, Infected, Unavailable, Indeterminate }

public sealed class UploadRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public string? Description { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    public string? Tags { get; set; }
    public Guid UploadToken { get; set; }
}

public sealed record UploadResult(int DocumentId, ContosoDashboard.Models.ScanStatus ScanStatus, bool IsDuplicate = false, string? Message = null);
public sealed record DocumentListItem(int DocumentId, string Title, ContosoDashboard.Models.DocumentCategory Category, long FileSize, DateTime UploadDateUtc, ContosoDashboard.Models.ScanStatus ScanStatus, string OriginalFileName);
public sealed record DocumentDownload(Stream Content, string FileName, string ContentType, long FileSize);
public sealed record DocumentAccess(bool CanView, bool CanDownload, bool CanManage);

public sealed class DocumentOperationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
