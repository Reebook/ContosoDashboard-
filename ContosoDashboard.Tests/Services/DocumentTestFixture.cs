using Azure.Core;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests.Services;

internal sealed class DocumentTestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public ApplicationDbContext Db { get; }
    public MemoryDocumentStorage Storage { get; } = new();
    public ConfigurableScanner Scanner { get; } = new();
    public RecordingAuditService Audit { get; } = new();
    public DocumentAuthorizationService Authorization { get; }
    public DocumentService Documents { get; }

    private DocumentTestFixture(SqliteConnection connection, ApplicationDbContext db, IOptions<DocumentOptions> options)
    {
        _connection = connection;
        Db = db;
        Authorization = new DocumentAuthorizationService(db);
        Documents = new DocumentService(db, Storage, Scanner, Authorization, Audit, options,
            NullLogger<DocumentService>.Instance);
    }

    public static async Task<DocumentTestFixture> CreateAsync(IInterceptor? interceptor = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        var db = new ApplicationDbContext(builder.Options);
        await db.Database.EnsureCreatedAsync();
        var options = Options.Create(new DocumentOptions
        {
            MaxFileSizeBytes = 25 * 1024 * 1024,
            AllowedExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png"],
            Storage = new DocumentStorageOptions { Provider = "Local", LocalPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) },
            Scanner = new DocumentScannerOptions { Provider = "Development" }
        });
        return new DocumentTestFixture(connection, db, options);
    }

    public UploadRequest ValidRequest(Guid? token = null) => new()
    {
        FileName = "minutes.pdf",
        ContentType = "application/pdf",
        Title = "Meeting minutes",
        Category = DocumentCategory.ProjectDocuments,
        ProjectId = null,
        UploadToken = token ?? Guid.NewGuid()
    };

    public static MemoryStream Pdf(string body = "sample") => new(System.Text.Encoding.ASCII.GetBytes($"%PDF-1.7\n{body}\n%%EOF"));

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class MemoryDocumentStorage : IDocumentStorage
{
    private readonly Dictionary<(StorageArea Area, string Name), byte[]> _files = new();
    public bool FailPutAfterWrite { get; set; }
    public bool Contains(StorageArea area, string name) => _files.ContainsKey((area, name));
    public int FileCount => _files.Count;

    public async Task PutAsync(StorageArea area, string blobName, Stream content, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _files[(area, blobName)] = ms.ToArray();
        if (FailPutAfterWrite) throw new IOException("Injected storage failure after write");
    }

    public Task<Stream> OpenReadAsync(string blobName, CancellationToken ct)
    {
        if (!_files.TryGetValue((StorageArea.Clean, blobName), out var bytes))
            throw new FileNotFoundException(blobName);
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task PromoteAsync(string blobName, CancellationToken ct)
    {
        if (!_files.Remove((StorageArea.Quarantine, blobName), out var bytes)) throw new FileNotFoundException(blobName);
        _files[(StorageArea.Clean, blobName)] = bytes;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string blobName, CancellationToken ct)
    {
        _files.Remove((StorageArea.Quarantine, blobName));
        _files.Remove((StorageArea.Clean, blobName));
        return Task.CompletedTask;
    }
}

internal sealed class ConfigurableScanner : IMalwareScanner
{
    public ScanOutcome Outcome { get; set; } = ScanOutcome.Clean;
    public Task<ScanOutcome> GetResultAsync(string quarantineBlobName, CancellationToken ct) => Task.FromResult(Outcome);
}

internal sealed class RecordingAuditService(ApplicationDbContext db) : IDocumentAuditService
{
    public async Task RecordAsync(DocumentAction action, ActivityOutcome outcome, int? actorId, int? documentId, string? titleSnapshot, string? detail)
    {
        db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = documentId,
            DocumentTitle = titleSnapshot ?? "",
            ActorUserId = actorId,
            Action = action,
            Outcome = outcome,
            Detail = detail,
            TimestampUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}

internal sealed class FailSecondSaveChangesInterceptor : SaveChangesInterceptor
{
    private int _calls;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _calls) == 2) throw new InvalidOperationException("Injected database write failure");
        return ValueTask.FromResult(result);
    }
}
