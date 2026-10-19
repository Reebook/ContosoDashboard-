using System.Net;
using System.Security.Claims;
using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace ContosoDashboard.Tests.Endpoints;

public sealed class DocumentUploadEndpointTests
{
    [Fact]
    public async Task Multipart_upload_returns_created_and_writes_audit()
    {
        await using var factory = new TestDocumentFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var form = CreatePdfForm();

        var response = await client.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Contains(await db.DocumentActivities.ToListAsync(), row => row.Action == DocumentAction.Upload && row.Outcome == ActivityOutcome.Success);
    }

    [Fact]
    public async Task Invalid_metadata_returns_bad_request_and_unsupported_type_returns_415()
    {
        await using var factory = new TestDocumentFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var missingTitle = CreatePdfForm(includeTitle: false);
        var badMetadata = await client.PostAsync("/api/documents", missingTitle);
        Assert.Equal(HttpStatusCode.BadRequest, badMetadata.StatusCode);

        using var unsupported = new MultipartFormDataContent();
        unsupported.Add(new StringContent("Unsafe file"), "title");
        unsupported.Add(new StringContent(nameof(DocumentCategory.Other)), "category");
        unsupported.Add(new StringContent(Guid.NewGuid().ToString()), "uploadToken");
        var bytes = new ByteArrayContent(Encoding.ASCII.GetBytes("MZ executable"));
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        unsupported.Add(bytes, "file", "payload.exe");
        var unsupportedResponse = await client.PostAsync("/api/documents", unsupported);
        Assert.Equal((HttpStatusCode)415, unsupportedResponse.StatusCode);
    }

    [Fact]
    public async Task Multipart_file_over_limit_returns_payload_too_large()
    {
        await using var factory = new TestDocumentFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Large file"), "title");
        form.Add(new StringContent(nameof(DocumentCategory.Other)), "category");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "uploadToken");
        var content = new ByteArrayContent(new byte[25 * 1024 * 1024 + 1]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "too-large.pdf");

        var response = await client.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static MultipartFormDataContent CreatePdfForm(bool includeTitle = true)
    {
        var form = new MultipartFormDataContent();
        if (includeTitle) form.Add(new StringContent("Meeting minutes"), "title");
        form.Add(new StringContent(nameof(DocumentCategory.ProjectDocuments)), "category");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "uploadToken");
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nfile\n%%EOF"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "minutes.pdf");
        return form;
    }
}

internal sealed class TestDocumentFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Documents:Storage:Provider"] = "Local",
            ["Documents:Storage:LocalPath"] = Path.Combine(Path.GetTempPath(), "contoso-test-" + Guid.NewGuid().ToString("N")),
            ["Documents:Scanner:Provider"] = "Development",
            ["Documents:Scanner:PollIntervalSeconds"] = "60"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            services.AddSingleton(connection);
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        });
    }
}

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "4"),
            new Claim(ClaimTypes.Name, "Ni Kang"),
            new Claim(ClaimTypes.Email, "ni.kang@contoso.com"),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("department", "Engineering")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
