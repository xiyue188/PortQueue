using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PortQueue.Api.Controllers;
using PortQueue.Api.Security;
using PortQueue.Api.Tests.Appointments;
using PortQueue.Application.Mobile;

namespace PortQueue.Api.Tests.Mobile;

[Trait("Category", "Database")]
public sealed class MobileEtaTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private const string AppointmentId = "9007199254740993";
    private static readonly int[] SubmissionStatuses = [200, 201];
    private WebApplicationFactory<Program> Factory() => fixture.Factory.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mobile:Enabled"] = "true" })));

    private async Task Setup(WebApplicationFactory<Program> factory)
    {
        await fixture.ApplyScriptAsync("mobile/01_schema.sql");
        await fixture.ApplyScriptAsync("mobile/02_permissions.sql");
        await fixture.ApplyScriptAsync("mobile/05_eta.sql");
        await DemoAccounts.SeedAsync(factory.Services);
        await Sql("UPDATE dbo.arrival_appointment SET appointment_status_code='APPROVED' WHERE appointment_id=" + AppointmentId);
    }

    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login")
        {
            Content = JsonContent.Create(new { username = "pq." + role, password = "123456" })
        };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return client;
    }

    private static object Payload(Guid requestId, DateTimeOffset eta, string appointmentId = AppointmentId, string description = "引航协调后预计抵达") =>
        new { requestId, appointmentId, proposedEta = eta, description };
    private static Task<HttpResponseMessage> Submit(HttpClient client, object payload) => client.PostAsJsonAsync("/api/mobile/reports/eta", payload);
    private static async Task<JsonElement> Created(HttpClient client, object payload)
    {
        using var response = await Submit(client, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Task<HttpResponseMessage> Decide(HttpClient client, JsonElement report, string decision = "ACCEPTED") =>
        client.PostAsJsonAsync("/api/mobile/reports/" + report.GetProperty("reportId").GetString() + "/decision",
            new { decision, comment = "调度核对意见", expectedVersion = report.GetProperty("version").GetString() });
    private async Task<string> Sql(string sql, params (string Name, object Value)[] args)
    {
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        using var command = new SqlCommand(sql, c);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    [Fact]
    public async Task SubmitProcessAndOriginalAccountReadbackPreserveFormalScheduleAndAudit()
    {
        await using var f = Factory();
        await Setup(f);
        await fixture.ApplyScriptAsync("12_refresh_sample_labels.sql");
        await fixture.ApplyScriptAsync("13_seed_port_scenarios.sql");
        var id = await Sql("SELECT TOP(1) CONVERT(varchar(20),appointment_id) FROM dbo.v_current_berth_plan WHERE plan_status_code='CONFIRMED' AND confirmed_at IS NOT NULL ORDER BY berth_plan_id");
        const string snapshot = """
            SELECT (SELECT a.* FROM dbo.arrival_appointment a WHERE a.appointment_id=@id FOR JSON PATH) appointment,
            (SELECT c.* FROM dbo.port_call c JOIN dbo.arrival_appointment a ON a.port_call_id=c.port_call_id WHERE a.appointment_id=@id FOR JSON PATH) portCall,
            (SELECT b.* FROM dbo.berth_plan b WHERE b.appointment_id=@id FOR JSON PATH) [plan],
            (SELECT r.* FROM dbo.berth_plan_revision r JOIN dbo.berth_plan b ON b.berth_plan_id=r.berth_plan_id WHERE b.appointment_id=@id FOR JSON PATH) revisions
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
            """;
        var before = await Sql(snapshot, ("@id", id));
        using var author = await Login(f, "recorder");
        using var dispatcher = await Login(f, "dispatcher");
        var request = Guid.NewGuid();
        var eta = DateTimeOffset.UtcNow.AddDays(2).ToOffset(TimeSpan.FromHours(8));
        var report = await Created(author, Payload(request, eta, id));
        Assert.Equal("ETA", report.GetProperty("reportType").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("photo").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("category").ValueKind);
        Assert.Equal("SUBMITTED", report.GetProperty("statusCode").GetString());
        Assert.Equal(eta, report.GetProperty("proposedEta").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, report.GetProperty("proposedEta").GetDateTimeOffset().Offset);
        Assert.Equal(HttpStatusCode.OK, (await Decide(dispatcher, report)).StatusCode);
        var saved = await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports/by-request/" + request);
        Assert.Equal("ACCEPTED", saved.GetProperty("statusCode").GetString());
        Assert.Equal("周航", saved.GetProperty("processedByName").GetString());
        Assert.Equal("调度核对意见", saved.GetProperty("processComment").GetString());
        Assert.Equal(eta, saved.GetProperty("proposedEta").GetDateTimeOffset());
        using var replay = await Submit(author, Payload(request, eta, id));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("ACCEPTED", (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("statusCode").GetString());
        var page = await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports?appointmentId=" + id);
        Assert.Contains(page.GetProperty("items").EnumerateArray(), row => row.GetProperty("reportId").GetString() == report.GetProperty("reportId").GetString() && row.GetProperty("proposedEta").GetDateTimeOffset() == eta);
        var overview = (await author.GetFromJsonAsync<VisitOverview>("/api/mobile/appointments/" + id + "/overview"))!;
        Assert.Equal(report.GetProperty("reportId").GetString(), overview.LatestAcceptedEta!.ReportId);
        Assert.Equal(eta, overview.LatestAcceptedEta.ProposedEta);
        Assert.NotNull(overview.PublishedPlan);
        Assert.Equal(before, await Sql(snapshot, ("@id", id)));
        Assert.Equal("2", await Sql("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='mobile_report' AND record_id=@id", ("@id", report.GetProperty("reportId").GetString()!)));
        Assert.Equal("0", await Sql("SELECT COUNT(*) FROM dbo.report_photo WHERE report_id=@id", ("@id", report.GetProperty("reportId").GetString()!)));
    }

    [Fact]
    public async Task ConcurrentDuplicateIsOneRecordOffsetEquivalentReplaysAndChangedContentConflicts()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "recorder");
        var request = Guid.NewGuid();
        var eta = DateTimeOffset.UtcNow.AddDays(3);
        var payload = Payload(request, eta);
        var responses = await Task.WhenAll(Submit(client, payload), Submit(client, payload));
        Assert.Equal(SubmissionStatuses, responses.Select(r => (int)r.StatusCode).Order().ToArray());
        var first = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
        var second = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("reportId").GetString(), second.GetProperty("reportId").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Submit(client, Payload(request, eta.ToOffset(TimeSpan.FromHours(8))))).StatusCode);
        foreach (var changed in new[] { Payload(request, eta.AddHours(1)), Payload(request, eta, description: "不同原因"), Payload(request, eta, "9007199254740994") })
            Assert.Equal(HttpStatusCode.Conflict, (await Submit(client, changed)).StatusCode);
        Assert.Equal("1", await Sql("SELECT COUNT(*) FROM dbo.mobile_report WHERE request_id=@request", ("@request", request)));
        Assert.Equal("1", await Sql("SELECT COUNT(*) FROM dbo.audit_log a JOIN dbo.mobile_report r ON a.table_name='mobile_report' AND a.record_id=CONVERT(varchar(100),r.report_id) WHERE r.request_id=@request", ("@request", request)));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("dispatcher")]
    public async Task AuthorizedInternalRolesCanSubmit(string role)
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, role);
        await Created(client, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public async Task CookieCsrfRolesAndOwnershipAreEnforced()
    {
        await using var f = Factory();
        await Setup(f);
        using var guest = f.CreateClient();
        var request = Guid.NewGuid();
        var payload = Payload(request, DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(guest, payload)).StatusCode);
        using var reviewer = await Login(f, "reviewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(reviewer, payload)).StatusCode);
        using var author = await Login(f, "recorder");
        var csrf = author.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        author.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(author, payload)).StatusCode);
        author.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var report = await Created(author, payload);
        var id = report.GetProperty("reportId").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync("/api/mobile/reports/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync("/api/mobile/reports/by-request/" + request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(author, report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.GetAsync("/api/mobile/reports?scope=all")).StatusCode);
        using var dispatcher = await Login(f, "dispatcher");
        dispatcher.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(dispatcher, report)).StatusCode);
    }

    [Fact]
    public async Task InvalidTimesMetadataAndIneligibleVisitCreateNothing()
    {
        await using var f = Factory();
        await Setup(f);
        using var client = await Login(f, "recorder");
        foreach (var eta in new[] { DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), DateTimeOffset.UtcNow.AddDays(31).ToString("O"), "not-a-time", "2030-01-01T10:00:00", "" })
        {
            var request = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, new { requestId = request, appointmentId = AppointmentId, proposedEta = eta, description = "有效说明" })).StatusCode);
            Assert.Equal("0", await Sql("SELECT COUNT(*) FROM dbo.mobile_report WHERE request_id=@request", ("@request", request)));
        }
        var future = DateTimeOffset.UtcNow.AddDays(1);
        foreach (var payload in new[] { Payload(Guid.Empty, future), Payload(Guid.NewGuid(), future, description: " "), Payload(Guid.NewGuid(), future, description: new string('字', 501)), Payload(Guid.NewGuid(), future, "invalid"), Payload(Guid.NewGuid(), future, "9007199254740994") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, new { requestId = Guid.NewGuid(), appointmentId = AppointmentId, description = "缺时间" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, new { requestId = Guid.NewGuid(), appointmentId = AppointmentId, proposedEta = future, description = "禁止伪造身份", submittedBy = "1" })).StatusCode);
    }

    [Fact]
    public async Task ExpiredEtaCannotBeAcceptedButCanBeRejectedWithoutChangingSchedule()
    {
        await using var f = Factory();
        await Setup(f);
        using var author = await Login(f, "recorder");
        using var dispatcher = await Login(f, "dispatcher");
        var report = await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));
        var id = report.GetProperty("reportId").GetString()!;
        // Administrator fixture models time passing; no production path bypasses the time rule.
        await Sql("UPDATE dbo.mobile_report SET proposed_eta=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE report_id=@id", ("@id", id));
        report = await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports/" + id);
        using var rejectedAccept = await Decide(dispatcher, report);
        Assert.Equal(HttpStatusCode.Conflict, rejectedAccept.StatusCode);
        Assert.Equal("ETA_EXPIRED", (await rejectedAccept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var unchanged = await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports/" + id);
        Assert.Equal("SUBMITTED", unchanged.GetProperty("statusCode").GetString());
        Assert.Equal(report.GetProperty("version").GetString(), unchanged.GetProperty("version").GetString());
        Assert.Equal("1", await Sql("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='mobile_report' AND record_id=@id", ("@id", id)));
        Assert.Equal(HttpStatusCode.OK, (await Decide(dispatcher, unchanged, "REJECTED")).StatusCode);
        Assert.Equal("REJECTED", (await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports/" + id)).GetProperty("statusCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(dispatcher, unchanged, "REJECTED")).StatusCode);
    }

    [Fact]
    public async Task OverviewUsesProcessingOrderThenReportIdAndDoesNotLeakAnotherVisit()
    {
        await using var f = Factory();
        await Setup(f);
        using var author = await Login(f, "recorder");
        using var dispatcher = await Login(f, "dispatcher");
        var older = await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));
        var newer = await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(2)));
        Assert.Equal(HttpStatusCode.OK, (await Decide(dispatcher, newer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Decide(dispatcher, older)).StatusCode);
        async Task<VisitOverview> Overview(string id = AppointmentId) => (await author.GetFromJsonAsync<VisitOverview>("/api/mobile/appointments/" + id + "/overview"))!;
        Assert.Equal(older.GetProperty("reportId").GetString(), (await Overview()).LatestAcceptedEta!.ReportId);
        await Sql("UPDATE dbo.mobile_report SET processed_at=SYSUTCDATETIME() WHERE report_id IN(@older,@newer)", ("@older", older.GetProperty("reportId").GetString()!), ("@newer", newer.GetProperty("reportId").GetString()!));
        Assert.Equal(newer.GetProperty("reportId").GetString(), (await Overview()).LatestAcceptedEta!.ReportId);
        var rejected = await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3)));
        Assert.Equal(HttpStatusCode.OK, (await Decide(dispatcher, rejected, "REJECTED")).StatusCode);
        await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(4)));
        Assert.Equal(newer.GetProperty("reportId").GetString(), (await Overview()).LatestAcceptedEta!.ReportId);
        Assert.Null((await Overview("9007199254740994")).LatestAcceptedEta);
    }

    [Theory]
    [InlineData(-1, "recorder", 52506)]
    [InlineData(31, "recorder", 52506)]
    [InlineData(1, "reviewer", 52503)]
    public async Task SqlProcedureIndependentlyEnforcesTimeAndRole(int days, string role, int expected)
    {
        await using var f = Factory();
        await Setup(f);
        var user = await Sql("SELECT user_id FROM dbo.user_account WHERE username=@name", ("@name", "pq." + role));
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        using var cmd = new SqlCommand("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_mobile_submit_eta @user_id=@user,@request_id=@request,@appointment_id=9007199254740993,@proposed_eta=@eta,@description=N'SQL boundary',@payload_hash=@hash;", c, tx);
        cmd.Parameters.AddWithValue("@user", long.Parse(user, System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@request", Guid.NewGuid());
        cmd.Parameters.Add("@eta", SqlDbType.DateTime2).Value = DateTime.UtcNow.AddDays(days);
        cmd.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = new byte[32];
        var error = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(expected, error.Number);
    }

    [Fact]
    public async Task EtaAuditFailureRollsBackSubmission()
    {
        await using var f = Factory();
        await Setup(f);
        using var author = await Login(f, "recorder");
        await Sql("CREATE OR ALTER TRIGGER dbo.trg_eta_test_audit_failure ON dbo.audit_log AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE table_name='mobile_report') THROW 52599,'Injected audit failure',1; END");
        var request = Guid.NewGuid();
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Submit(author, Payload(request, DateTimeOffset.UtcNow.AddDays(1)))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync("/api/mobile/reports/by-request/" + request)).StatusCode);
            Assert.Equal("0", await Sql("SELECT COUNT(*) FROM dbo.mobile_report WHERE request_id=@request", ("@request", request)));
        }
        finally { await Sql("DROP TRIGGER dbo.trg_eta_test_audit_failure"); }
    }

    [Fact]
    public async Task MigrationIsRepeatableAndProbeCanSubmitReplayButCannotWriteTables()
    {
        await using var f = Factory();
        await Setup(f);
        await fixture.ApplyScriptAsync("mobile/05_eta.sql");
        await fixture.ApplyScriptAsync("mobile/04_smoke_tests.sql");
        await fixture.ApplyScriptAsync("mobile/06_eta_smoke_tests.sql");
        await using var c = new SqlConnection(fixture.ConnectionString);
        await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        using var cmd = new SqlCommand("""
            DECLARE @user bigint=(SELECT user_id FROM dbo.user_account WHERE username='pq.recorder'),@request uniqueidentifier=NEWID(),@eta datetime2(7)=DATEADD(day,30,SYSUTCDATETIME()),@hash binary(32)=0x01;
            EXECUTE AS USER='portqueue_probe';
            EXEC dbo.usp_mobile_submit_eta @user,@request,9007199254740993,@eta,N'boundary and replay',@hash;
            REVERT;
            -- Model an older saved record and subsequently closed visit only inside this rolled-back fixture.
            UPDATE dbo.mobile_report SET proposed_eta=DATEADD(day,-1,SYSUTCDATETIME()) WHERE submitted_by=@user AND request_id=@request;
            UPDATE dbo.arrival_appointment SET appointment_status_code='COMPLETED' WHERE appointment_id=9007199254740993;
            SET @eta=DATEADD(day,-1,SYSUTCDATETIME());
            EXECUTE AS USER='portqueue_probe';
            EXEC dbo.usp_mobile_submit_eta @user,@request,9007199254740993,@eta,N'boundary and replay',@hash;
            REVERT;
            SELECT COUNT(*) FROM dbo.audit_log a JOIN dbo.mobile_report r ON a.table_name='mobile_report' AND a.record_id=CONVERT(varchar(100),r.report_id) WHERE r.request_id=@request;
            """, c, tx);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var id = reader.GetInt64(0);
        Assert.False(reader.GetBoolean(1));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(id, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact]
    public async Task DecisionAuditFailureLeavesEtaUnprocessed()
    {
        await using var f = Factory();
        await Setup(f);
        using var author = await Login(f, "recorder");
        using var dispatcher = await Login(f, "dispatcher");
        var report = await Created(author, Payload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));
        await Sql("CREATE OR ALTER TRIGGER dbo.trg_eta_test_decision_audit_failure ON dbo.audit_log AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE table_name='mobile_report' AND operation_type='UPDATE') THROW 52599,'Injected decision audit failure',1; END");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Decide(dispatcher, report)).StatusCode);
            var saved = await author.GetFromJsonAsync<JsonElement>("/api/mobile/reports/" + report.GetProperty("reportId").GetString());
            Assert.Equal("SUBMITTED", saved.GetProperty("statusCode").GetString());
            Assert.Equal(report.GetProperty("version").GetString(), saved.GetProperty("version").GetString());
            Assert.Equal(JsonValueKind.Null, saved.GetProperty("processedAt").ValueKind);
        }
        finally { await Sql("DROP TRIGGER dbo.trg_eta_test_decision_audit_failure"); }
    }
}
