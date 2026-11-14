using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Api.Controllers;
using PortQueue.Api.Security;
using PortQueue.Api.Tests.Appointments;
using PortQueue.Application.Mobile.Reports;
using PortQueue.Infrastructure.Mobile.Reports;
namespace PortQueue.Api.Tests.Mobile;
[Trait("Category", "Database")]
public sealed class MobileReportTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly int[] SubmitStatuses = [200, 201];
    private static readonly int[] DecisionStatuses = [200, 409];
    private readonly string _photos = Path.Combine(Path.GetTempPath(), "pq-m2-test-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> Factory() => fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { { "Mobile:Enabled", "true" }, { "Mobile:PhotoDirectory", _photos } })));
    private async Task Setup(WebApplicationFactory<Program> factory)
    {
        await fixture.ApplyScriptAsync("mobile/01_schema.sql");
        await fixture.ApplyScriptAsync("mobile/02_permissions.sql");
        await fixture.ApplyScriptAsync("mobile/05_eta.sql");
        await DemoAccounts.SeedAsync(factory.Services);
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        using var cmd = new SqlCommand("UPDATE dbo.arrival_appointment SET appointment_status_code='APPROVED' WHERE appointment_id=9007199254740993", c);
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> f, string role)
    {
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var s = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/session/login") { Content = JsonContent.Create(new { username = "pq." + role, password = "123456" }) };
        req.Headers.Add("X-CSRF-TOKEN", s.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return client;
    }
    private static byte[] Jpeg(int width = 20)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Mobile", "Fixtures", $"jpeg-{width}.jpg"));
    }
    private static PhotoMetadata Metadata() => new(Guid.NewGuid(), "9007199254740993", "SITE", "现场照片", DateTimeOffset.UtcNow, null);
    private static Task<HttpResponseMessage> Upload(HttpClient client, PhotoMetadata data, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(data, Options)), "metadata");
        content.Add(new ByteArrayContent(bytes), "photo", "photo.jpg");
        return client.PostAsync("/api/mobile/reports/photo", content);
    }
    [Fact]
    public async Task PhotoEndpointRequiresSubmitRoleBeforeParsingMultipart()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "reviewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/mobile/reports/photo", new MultipartFormDataContent())).StatusCode);
    }
    [Fact]
    public async Task DuplicateConcurrentRequestReturnsOneRecordDifferentContentConflictsAndProtectedPhotoIsQueryable()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "recorder");
        var data = Metadata();
        var bytes = Jpeg();
        var responses = await Task.WhenAll(Upload(client, data, bytes), Upload(client, data, bytes));
        Assert.Equal(SubmitStatuses, responses.Select(r => (int)r.StatusCode).Order().ToArray());
        var first = (await responses[0].Content.ReadFromJsonAsync<ReportDetail>())!;
        var second = (await responses[1].Content.ReadFromJsonAsync<ReportDetail>())!;
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.False(first.CanProcess);
        Assert.Equal(8, Convert.FromBase64String(first.Version).Length);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, data with
        {
            Description = "不同内容"
        }, bytes)).StatusCode);
        Assert.Equal(first.ReportId, (await client.GetFromJsonAsync<ReportDetail>("/api/mobile/reports/by-request/" + data.RequestId))!.ReportId);
        var photo = await client.GetAsync("/api/mobile/photos/" + first.Photo!.PhotoId);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal(bytes, await photo.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", photo.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/reports?scope=all")).StatusCode);
        Assert.Single(Directory.GetFiles(_photos, "*.jpg"));
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.mobile_report WHERE request_id=@request", c);
        cmd.Parameters.AddWithValue("@request", data.RequestId);
        Assert.Equal(1, await cmd.ExecuteScalarAsync());
    }
    [Fact]
    public async Task PermissionIsolationAndConcurrentDecisionAreEnforced()
    {
        await using var f = Factory();
        await Setup(f);
        using var author = await Login(f, "recorder");
        using var other = await Login(f, "reviewer");
        using var dispatcher = await Login(f, "dispatcher");
        var data = Metadata();
        var response = await Upload(author, data, Jpeg());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ReportDetail>())!;
        foreach (var path in new[] { "reports/" + report.ReportId, "photos/" + report.Photo!.PhotoId, "reports/by-request/" + data.RequestId })
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/mobile/" + path)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<ReportPage>("/api/mobile/reports"))!.Items);
        var decision = new
        {
            decision = "ACCEPTED",
            comment = "已核实",
            expectedVersion = report.Version
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsJsonAsync("/api/mobile/reports/" + report.ReportId + "/decision", decision)).StatusCode);
        var results = await Task.WhenAll(dispatcher.PostAsJsonAsync("/api/mobile/reports/" + report.ReportId + "/decision", decision), dispatcher.PostAsJsonAsync("/api/mobile/reports/" + report.ReportId + "/decision", decision));
        Assert.Equal(DecisionStatuses, results.Select(r => (int)r.StatusCode).Order().ToArray());
        var updated = (await author.GetFromJsonAsync<ReportDetail>("/api/mobile/reports/" + report.ReportId))!;
        Assert.Equal("ACCEPTED", updated.StatusCode);
        Assert.NotNull(updated.ProcessedAt);
        Assert.Equal("周航", updated.ProcessedByName);
        Assert.Equal("已核实", updated.ProcessComment);
        Assert.NotEqual(report.Version, updated.Version);
    }
    [Fact]
    public async Task InvalidImagesAndRejectedTransactionDoNotPersistFiles()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "recorder");
        foreach (var (bytes, status) in new[] { (new byte[] { 255, 216, 0, 0, 255, 217 }, 415), (new byte[] { 1, 2, 3 }, 415), (new byte[5242881], 413), (Jpeg(1601), 400) })
            Assert.Equal(status, (int)(await Upload(client, Metadata(), bytes)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, Metadata() with
        {
            AppointmentId = "9007199254740994"
        }, Jpeg())).StatusCode);
        Assert.False(Directory.Exists(_photos) && Directory.GetFiles(_photos, "*.jpg").Length > 0);
    }
    [Fact]
    public async Task ReadableJpegHeaderWithDamagedScanIsRejectedByActualDecoder()
    {
        var broken=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Mobile","Fixtures","jpeg-damaged-scan.jpg"));
        using var stream=new MemoryStream(broken);
        var info=StbImageSharp.ImageInfo.FromStream(stream);
        Assert.NotNull(info);
        Assert.Equal(20,info.Value.Width);
        // The pinned decoder throws InvalidOperationException at actual SOS/entropy decoding.
        Assert.Throws<InvalidOperationException>(()=>StbImageSharp.ImageResult.FromMemory(broken,StbImageSharp.ColorComponents.RedGreenBlue));
        await using var f=Factory();
        await Setup(f);
        using var client=await Login(f,"recorder");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await Upload(client,Metadata(),broken)).StatusCode);
        Assert.False(Directory.Exists(_photos));
    }
    [Fact]
    public async Task ProbeHasOnlyNarrowWriteProcedures()
    {
        await using var f = Factory();
        await Setup(f);
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        using var cmd = new SqlCommand("EXECUTE AS USER='portqueue_probe'; SELECT HAS_PERMS_BY_NAME('dbo.mobile_report','OBJECT','INSERT'),HAS_PERMS_BY_NAME('dbo.report_photo','OBJECT','UPDATE'),HAS_PERMS_BY_NAME('dbo.audit_log','OBJECT','INSERT'),HAS_PERMS_BY_NAME('dbo.usp_mobile_submit_photo','OBJECT','EXECUTE'),HAS_PERMS_BY_NAME('dbo.usp_mobile_decide','OBJECT','EXECUTE');REVERT;", c);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var i = 0; i < 3; i++)
            Assert.Equal(0, reader.GetInt32(i));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
    }
    [Fact]
    public async Task LostCommitAcknowledgementNeverDeletesCommittedPhoto()
    {
        await using var f = Factory().WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IReportTransactionCommitter, LostAcknowledgement>()));
        await Setup(f);
        using var client = await Login(f, "recorder");
        var data = Metadata();
        var bytes = Jpeg();
        var response = await Upload(client, data, bytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ReportDetail>())!;
        Assert.Equal(bytes, await client.GetByteArrayAsync("/api/mobile/photos/" + report.Photo!.PhotoId));
        Assert.Single(Directory.GetFiles(_photos, "*.jpg"));
    }
    private sealed class LostAcknowledgement : IReportTransactionCommitter
    {
        public async Task CommitAsync(SqlTransaction transaction, CancellationToken token)
        {
            await transaction.CommitAsync(token);
            throw new IOException("Injected lost commit acknowledgement");
        }
    }
    [Fact]
    public async Task AuditFailureRollsBackBothRowsAndCompensatesOnlyNewFile()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "recorder");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var create = new SqlCommand("CREATE OR ALTER TRIGGER dbo.trg_m2_test_audit_failure ON dbo.audit_log AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE table_name='mobile_report') THROW 52599,'Injected audit failure',1; END", connection);
        await create.ExecuteNonQueryAsync();
        var metadata = Metadata();
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, metadata, Jpeg())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/mobile/reports/by-request/" + metadata.RequestId)).StatusCode);
            Assert.Empty(Directory.GetFiles(_photos, "*.jpg"));
        }
        finally
        {
            using var drop = new SqlCommand("DROP TRIGGER dbo.trg_m2_test_audit_failure", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
