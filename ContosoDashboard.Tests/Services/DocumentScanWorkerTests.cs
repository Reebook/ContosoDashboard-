using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentScanWorkerTests
{
    [Theory]
    [InlineData(ScanOutcome.Unavailable)]
    [InlineData(ScanOutcome.Indeterminate)]
    public async Task Unavailable_or_indeterminate_scan_keeps_blob_quarantined_and_schedules_retry(ScanOutcome outcome)
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var uploaded = await fixture.Documents.UploadAsync(4, fixture.ValidRequest(), DocumentTestFixture.Pdf(), CancellationToken.None);
        var document = await fixture.Db.Documents.SingleAsync();
        var quarantineName = document.BlobName;
        fixture.Scanner.Outcome = outcome;

        await DocumentScanWorker.ProcessPendingAsync(fixture.Db, fixture.Storage, fixture.Scanner, fixture.Audit, CancellationToken.None);

        document = await fixture.Db.Documents.SingleAsync();
        Assert.Equal(ScanStatus.Pending, document.ScanStatus);
        Assert.True(document.ScanAttemptCount > 0);
        Assert.NotNull(document.NextScanAttemptUtc);
        Assert.True(fixture.Storage.Contains(StorageArea.Quarantine, quarantineName));
        Assert.Null(await fixture.Documents.OpenForDownloadAsync(4, uploaded.DocumentId, inline: false));
    }

    [Fact]
    public async Task Infected_file_is_rejected_and_quarantine_content_removed()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        var uploaded = await fixture.Documents.UploadAsync(4, fixture.ValidRequest(), DocumentTestFixture.Pdf(), CancellationToken.None);
        fixture.Scanner.Outcome = ScanOutcome.Infected;

        await DocumentScanWorker.ProcessPendingAsync(fixture.Db, fixture.Storage, fixture.Scanner, fixture.Audit, CancellationToken.None);

        var document = await fixture.Db.Documents.SingleAsync();
        Assert.Equal(ScanStatus.Rejected, document.ScanStatus);
        Assert.Equal(0, fixture.Storage.FileCount);
        Assert.Null(await fixture.Documents.OpenForDownloadAsync(4, uploaded.DocumentId, inline: false));
    }

    [Fact]
    public async Task Clean_result_promotes_quarantined_blob_and_records_audit()
    {
        await using var fixture = await DocumentTestFixture.CreateAsync();
        await fixture.Documents.UploadAsync(4, fixture.ValidRequest(), DocumentTestFixture.Pdf(), CancellationToken.None);
        var document = await fixture.Db.Documents.SingleAsync();
        var name = document.BlobName;
        fixture.Scanner.Outcome = ScanOutcome.Clean;

        await DocumentScanWorker.ProcessPendingAsync(fixture.Db, fixture.Storage, fixture.Scanner, fixture.Audit, CancellationToken.None);

        Assert.Equal(ScanStatus.Clean, (await fixture.Db.Documents.SingleAsync()).ScanStatus);
        Assert.True(fixture.Storage.Contains(StorageArea.Clean, name));
        Assert.False(fixture.Storage.Contains(StorageArea.Quarantine, name));
        Assert.Contains(await fixture.Db.DocumentActivities.ToListAsync(), item => item.Action == DocumentAction.ScanResult && item.Outcome == ActivityOutcome.Success);
    }
}
