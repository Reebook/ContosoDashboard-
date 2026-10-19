using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public sealed class DocumentActivity
{
    [Key]
    public long ActivityId { get; set; }

    // Deliberately no document foreign key: audit history survives hard deletion.
    public int? DocumentId { get; set; }

    [Required, MaxLength(255)]
    public string DocumentTitle { get; set; } = string.Empty;

    public int? ActorUserId { get; set; }

    [Required]
    public DocumentAction Action { get; set; }

    [Required]
    public ActivityOutcome Outcome { get; set; }

    [MaxLength(500)]
    public string? Detail { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
