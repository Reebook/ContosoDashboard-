using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public enum DocumentCategory
{
    ProjectDocuments,
    TeamResources,
    PersonalFiles,
    Reports,
    Presentations,
    Other
}

public enum ScanStatus
{
    Pending,
    Clean,
    Rejected
}

public enum DocumentAction
{
    Upload,
    Download,
    Preview,
    Delete,
    ShareGrant,
    ShareRevoke,
    Replace,
    EditMetadata,
    Denied,
    ScanResult
}

public enum ActivityOutcome
{
    Success,
    Failure,
    Denied
}

public sealed class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required, MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    public DocumentCategory Category { get; set; }

    [MaxLength(500)]
    public string? Tags { get; set; }

    public int? ProjectId { get; set; }

    [Required]
    public int UploaderId { get; set; }

    [Required, MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [Required, MaxLength(200)]
    public string BlobName { get; set; } = string.Empty;

    [Required]
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;

    public int ScanAttemptCount { get; set; }

    public DateTime? NextScanAttemptUtc { get; set; }

    public DateTime UploadDateUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDateUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public Guid UploadToken { get; set; }

    [Required, MaxLength(64)]
    public string UploadHash { get; set; } = string.Empty;

    public User Uploader { get; set; } = null!;
    public Project? Project { get; set; }
}
