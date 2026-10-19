using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentUploadTests
{
    [Fact]
    public async Task Upload_rejects_disallowed_extension()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var request = fixture.ValidRequest();
        request.FileName = "payload.exe";
        request.ContentType = "application/octet-stream";

        var error = await Assert.ThrowsAsync<DocumentOperationException>(() =>
            fixture.Documents.UploadAsync(4, request, DocumentTestFixture.Pdf(), CancellationToken.None));

        Assert.Equal(415, error.StatusCode);
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Upload_rejects_file_over_25_megabytes()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var request = fixture.ValidRequest();
        await using var content = new MemoryStream(new byte[25 * 1024 * 1024 + 1]);

        var error = await Assert.ThrowsAsync<DocumentOperationException>(() =>
            fixture.Documents.UploadAsync(4, request, content, CancellationToken.None));

        Assert.Equal(413, error.StatusCode);
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Upload_is_idempotent_for_same_token_and_payload_and_conflicts_for_changed_payload()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var token = Guid.NewGuid();
        var first = await fixture.Documents.UploadAsync(4, fixture.ValidRequest(token), DocumentTestFixture.Pdf(), CancellationToken.None);
        var retry = await fixture.Documents.UploadAsync(4, fixture.ValidRequest(token), DocumentTestFixture.Pdf(), CancellationToken.None);

        Assert.Equal(first.DocumentId, retry.DocumentId);
        Assert.True(retry.IsDuplicate);
        Assert.Single(await fixture.Db.Documents.ToListAsync());

        var changed = await Assert.ThrowsAsync<DocumentOperationException>(() =>
            fixture.Documents.UploadAsync(4, fixture.ValidRequest(token), DocumentTestFixture.Pdf("different"), CancellationToken.None));
        Assert.Equal(409, changed.StatusCode);
    }

    [Fact]
    public async Task Upload_removes_quarantine_blob_when_database_commit_fails()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync(new FailSecondSaveChangesInterceptor());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Documents.UploadAsync(4, fixture.ValidRequest(), DocumentTestFixture.Pdf(), CancellationToken.None));

        Assert.Empty(await fixture.Db.Documents.ToListAsync());
        Assert.Equal(0, fixture.Storage.FileCount);
    }

    [Fact]
    public async Task Uploaded_content_is_pending_and_unavailable_until_worker_promotes_clean_file()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var uploaded = await fixture.Documents.UploadAsync(4, fixture.ValidRequest(), DocumentTestFixture.Pdf(), CancellationToken.None);

        Assert.Equal(ScanStatus.Pending, uploaded.ScanStatus);
        Assert.Null(await fixture.Documents.OpenForDownloadAsync(4, uploaded.DocumentId, inline: false));

        fixture.Scanner.Outcome = ScanOutcome.Clean;
        await DocumentScanWorker.ProcessPendingAsync(fixture.Db, fixture.Storage, fixture.Scanner, fixture.Audit, CancellationToken.None);

        var file = await fixture.Documents.OpenForDownloadAsync(4, uploaded.DocumentId, inline: false);
        Assert.NotNull(file);
        await file!.Content.DisposeAsync();
        Assert.Equal(ScanStatus.Clean, (await fixture.Db.Documents.SingleAsync()).ScanStatus);
    }
}
