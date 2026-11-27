using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Api.Controllers;
using PortQueue.Api.Tests.Appointments;
using PortQueue.Application.Accounts;
using PortQueue.Application.Anchorages;

namespace PortQueue.Api.Tests.Anchorages;

[Trait("Category", "Database")]
public sealed class AnchorageOperationTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Theory]
    [InlineData("/api/anchorages/1/entries", "POST")]
    [InlineData("/api/anchorage-stays/1/leave", "POST")]
    [InlineData("/api/anchorage-stays/1/estimate", "PUT")]
    [InlineData("/api/anchorages/entry-candidates", "GET")]
    public async Task OperationalEndpointsRequireAuthentication(string path, string method)
    {
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("DISPATCHER")]
    [InlineData("RECORDER")]
    public async Task EnterEstimateLeaveCommitsFactsAndAuditForTheRealActor(string role)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, role); using var loggedIn = client;
        var seed = await Seed();
        var entered = await Enter(client, seed);
        Assert.Equal(seed.Appointment.ToString(CultureInfo.InvariantCulture), entered.AppointmentId);
        var initial = (await client.GetFromJsonAsync<AnchorageStayDetail>($"/api/anchorage-stays/{entered.QueueEntryId}"))!;
        Assert.Null(initial.ManualEstimatedBerthingAt); Assert.Equal("0", initial.EstimateVersion);
        var at = Time(120);
        using var saved = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/estimate",
            new { manualEstimatedBerthingAt = at, expectedVersion = "0", reason = "等待泊位核验" }, "PUT");
        Assert.Equal(role == "RECORDER" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, saved.StatusCode);
        await using var db = new SqlConnection(fixture.ConnectionString);
        if (role != "RECORDER")
        {
            var estimate = (await saved.Content.ReadFromJsonAsync<AnchorageEstimateResult>())!;
            Assert.Equal("1", estimate.EstimateVersion);
            Assert.Equal(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture), estimate.ManualEstimatedBerthingAt);
            using var stale = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/estimate",
                new { manualEstimatedBerthingAt = Time(150), expectedVersion = "0", reason = "过期版本" }, "PUT");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE record_id=@id AND table_name='anchorage_queue_entry' AND JSON_VALUE(new_data_json,'$.operation_reason')=N'等待泊位核验' AND operator_id=@actor", new { id = entered.QueueEntryId, actor }));
            using var cleared = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/estimate",
                new { manualEstimatedBerthingAt = (string?)null, expectedVersion = "1", reason = "泊位待定" }, "PUT");
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.Equal("2", (await cleared.Content.ReadFromJsonAsync<AnchorageEstimateResult>())!.EstimateVersion);
        }
        using var leave = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/leave", new { eventTime = Time(-30), remark = "已离开" });
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        var detail = (await client.GetFromJsonAsync<AnchorageStayDetail>($"/api/anchorage-stays/{entered.QueueEntryId}"))!;
        Assert.Equal("LEFT", detail.StatusCode); Assert.NotNull(detail.LeftAt); Assert.Equal(2, detail.Events.Count);
        var queue = (await client.GetFromJsonAsync<AnchorageQueueSnapshot>($"/api/anchorages/{seed.Anchorage}/queue"))!;
        Assert.Equal(0, queue.OccupiedCount); Assert.Empty(queue.Items);
        var facts = await db.QuerySingleAsync<(int Events, int Links, int Audits)>("""
            SELECT (SELECT COUNT(*) FROM dbo.v_port_event WHERE queue_entry_id=@id AND recorded_by=@actor),
                (SELECT COUNT(*) FROM dbo.anchorage_event_link WHERE queue_entry_id=@id),
                (SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='port_event' AND operator_id=@actor
                    AND JSON_VALUE(new_data_json,'$.queue_entry_id')=@id);
            """, new { id = entered.QueueEntryId, actor });
        Assert.Equal((2, 2, 2), facts);
        var next = await Enter(client, seed, Time(-20));
        using var lateLeave = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/leave", new { eventTime = Time(-10) });
        Assert.Equal(HttpStatusCode.Conflict, lateLeave.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<AnchorageStayDetail>($"/api/anchorage-stays/{next.QueueEntryId}"))!.LeftAt);
    }

    [Theory]
    [InlineData("REVIEWER")]
    [InlineData("RECORDER")]
    public async Task HttpAndDatabaseBothEnforceWriteRoles(string role)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, role); using var loggedIn = client;
        var seed = await Seed();
        using var enter = await Send(client, $"/api/anchorages/{seed.Anchorage}/entries", new { appointmentId = seed.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-60) });
        Assert.Equal(role == "REVIEWER" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, enter.StatusCode);
        await using var db = new SqlConnection(fixture.ConnectionString);
        var estimateError = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_set_anchorage_estimate 1,NULL,0,N'reason',@actor", new { actor }));
        Assert.Equal(52201, estimateError.Number);
        using var estimate = await Send(client, "/api/anchorage-stays/1/estimate", new { manualEstimatedBerthingAt = Time(60), expectedVersion = "0", reason = "reason" }, "PUT");
        Assert.Equal(HttpStatusCode.Forbidden, estimate.StatusCode);
        if (role == "REVIEWER")
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_ENTER',@at,@actor", new { ap = seed.Appointment, a = seed.Anchorage, at = DateTime.UtcNow.AddHours(-1), actor }));
            Assert.Equal(52201, error.Number);
        }
    }

    [Fact]
    public async Task CapacityShipOccupancyAndDisabledFacilitiesAreRecheckedAtCommit()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "DISPATCHER"); using var loggedIn = client;
        var one = await Seed(capacity: 1); var two = await Seed();
        var entered = await Enter(client, one);
        using var duplicate = await Send(client, $"/api/anchorages/{one.Anchorage}/entries", new { appointmentId = one.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-50) });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var db = new SqlConnection(fixture.ConnectionString);
        var sameShip = await Seed(shipId: one.Ship);
        using var physicalDuplicate = await Send(client, $"/api/anchorages/{sameShip.Anchorage}/entries", new { appointmentId = sameShip.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-50) });
        Assert.Equal(HttpStatusCode.Conflict, physicalDuplicate.StatusCode);
        await db.ExecuteAsync("UPDATE dbo.anchorage_queue_entry SET queue_status_code='CANCELLED',queue_position=NULL WHERE queue_entry_id=@id; UPDATE dbo.arrival_appointment SET appointment_status_code='CANCELLED' WHERE appointment_id=@ap;", new { id = entered.QueueEntryId, ap = one.Appointment });
        using var full = await Send(client, $"/api/anchorages/{one.Anchorage}/entries", new { appointmentId = two.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-50) });
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Contains("ANCHORAGE_CAPACITY_FULL", await full.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await db.ExecuteAsync("UPDATE dbo.anchorage SET status_code='DISABLED' WHERE anchorage_id=@id", new { id = one.Anchorage });
        using var leave = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/leave", new { eventTime = Time(-30) });
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        using var disabled = await Send(client, $"/api/anchorages/{one.Anchorage}/entries", new { appointmentId = two.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-20) });
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.anchorage_queue_entry WHERE anchorage_id=@id", new { id = one.Anchorage }));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("approval")]
    [InlineData("fraction")]
    [InlineData("offset")]
    [InlineData("pending")]
    [InlineData("foreign")]
    [InlineData("csrf")]
    [InlineData("spoof")]
    public async Task InvalidEntryLeavesNoStayEventOrAudit(string kind)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "ADMIN"); using var loggedIn = client;
        var seed = await Seed(); await using var db = new SqlConnection(fixture.ConnectionString);
        if (kind == "pending") await db.ExecuteAsync("UPDATE dbo.arrival_appointment SET appointment_status_code='PENDING' WHERE appointment_id=@id", new { id = seed.Appointment });
        if (kind == "foreign") await db.ExecuteAsync("INSERT dbo.port(port_code,port_name,country_code,timezone,status_code) VALUES('S4-OTHER',N'外港','CN','Asia/Shanghai','ENABLED'); UPDATE dbo.anchorage SET port_id=SCOPE_IDENTITY() WHERE anchorage_id=@id", new { id = seed.Anchorage });
        var time = kind switch { "future" => Time(60), "approval" => Time(-4000), "fraction" => "2026-11-20T12:00:00.5Z", "offset" => "2026-11-20T12:00:00", _ => Time(-60) };
        object body = kind == "spoof" ? new { appointmentId = seed.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = time, operatorId = "1" }
            : new { appointmentId = seed.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = time };
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_event");
        using var response = await Send(client, $"/api/anchorages/{seed.Anchorage}/entries", body, csrf: kind != "csrf");
        Assert.Equal(kind == "pending" ? HttpStatusCode.Conflict : kind == "foreign" ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.anchorage_queue_entry WHERE appointment_id=@id", new { id = seed.Appointment }));
        Assert.Equal(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_event"));
    }

    [Fact]
    public async Task ReverseLeaveAndAuditFailureRollBackEveryFact()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "RECORDER"); using var loggedIn = client;
        var seed = await Seed(); var entered = await Enter(client, seed);
        using var reverse = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/leave", new { eventTime = Time(-90) });
        Assert.Equal(HttpStatusCode.BadRequest, reverse.StatusCode);
        await using var db = new SqlConnection(fixture.ConnectionString);
        await db.ExecuteAsync("CREATE TRIGGER dbo.trg_test_anchorage_audit_failure ON dbo.audit_log AFTER INSERT AS THROW 52888,'Test audit failure',1;");
        try
        {
            using var failed = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/leave", new { eventTime = Time(-30) });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.v_port_event WHERE queue_entry_id=@id", new { id = entered.QueueEntryId }));
            Assert.Equal("WAITING", await db.ExecuteScalarAsync<string>("SELECT queue_status_code FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@id", new { id = entered.QueueEntryId }));
        }
        finally { await db.ExecuteAsync("DROP TRIGGER dbo.trg_test_anchorage_audit_failure;"); }
    }

    [Fact]
    public async Task CandidatesPaginateRealApprovedVisitsAndExcludeOccupiedShips()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "DISPATCHER"); using var loggedIn = client;
        var seeds = new List<TestSeed>();
        for (var i = 0; i < 7; i++) seeds.Add(await Seed());
        await using var db = new SqlConnection(fixture.ConnectionString);
        foreach (var seed in seeds) await db.ExecuteAsync("UPDATE dbo.ship SET ship_name=N'候选分页测试' WHERE ship_id=@id", new { id = seed.Ship });
        var first = (await client.GetFromJsonAsync<AnchorageCandidatePage>("/api/anchorages/entry-candidates?keyword=候选分页测试&pageSize=3"))!;
        var second = (await client.GetFromJsonAsync<AnchorageCandidatePage>("/api/anchorages/entry-candidates?keyword=候选分页测试&pageSize=3&page=2"))!;
        Assert.Equal(7, first.TotalCount); Assert.Equal(3, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(i => i.AppointmentId).Intersect(second.Items.Select(i => i.AppointmentId)));
        await Enter(client, seeds[0]);
        var same = await Seed(shipId: seeds[0].Ship);
        var after = (await client.GetFromJsonAsync<AnchorageCandidatePage>("/api/anchorages/entry-candidates?keyword=候选分页测试&pageSize=100"))!;
        Assert.Equal(6, after.TotalCount);
        Assert.DoesNotContain(after.Items, i => i.AppointmentId == same.Appointment.ToString(CultureInfo.InvariantCulture));
        var literal = (await client.GetFromJsonAsync<AnchorageCandidatePage>("/api/anchorages/entry-candidates?keyword=%25_%5B"))!;
        Assert.Empty(literal.Items);
        using var invalid = await client.GetAsync("/api/anchorages/entry-candidates?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCapacityAndEstimateWritesHaveOneWinner()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "DISPATCHER"); using var loggedIn = client;
        var one = await Seed(capacity: 1); var two = await Seed();
        await using var gate = new SqlConnection(fixture.ConnectionString); await gate.OpenAsync();
        using (var transaction = gate.BeginTransaction())
        {
            await gate.ExecuteAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; IF @r<0 THROW 53999,'Test lock failed',1;", transaction: transaction);
            var a = Send(client, $"/api/anchorages/{one.Anchorage}/entries", new { appointmentId = one.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-60) });
            var b = Send(client, $"/api/anchorages/{one.Anchorage}/entries", new { appointmentId = two.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = Time(-60) });
            await Task.Delay(150); Assert.False(a.IsCompleted); Assert.False(b.IsCompleted); transaction.Commit();
            var responses = await Task.WhenAll(a, b);
            Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
            foreach (var response in responses) response.Dispose();
        }
        var id = await gate.ExecuteScalarAsync<long>("SELECT queue_entry_id FROM dbo.anchorage_queue_entry WHERE anchorage_id=@id", new { id = one.Anchorage });
        var results = await Task.WhenAll(
            Send(client, $"/api/anchorage-stays/{id}/estimate", new { manualEstimatedBerthingAt = Time(120), expectedVersion = "0", reason = "甲计划" }, "PUT"),
            Send(client, $"/api/anchorage-stays/{id}/estimate", new { manualEstimatedBerthingAt = Time(180), expectedVersion = "0", reason = "乙计划" }, "PUT"));
        Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var result in results) result.Dispose();
        Assert.Equal(1, await gate.ExecuteScalarAsync<long>("SELECT estimate_version FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@id", new { id }));
        Assert.Equal(1, await gate.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='anchorage_queue_entry' AND record_id=CONVERT(varchar(100),@id) AND JSON_VALUE(new_data_json,'$.operation_reason') IS NOT NULL", new { id }));
    }

    [Fact]
    public async Task MinimumPermissionUserCanExecuteWritesAndCandidates()
    {
        var seed = await Seed(); await using var db = new SqlConnection(fixture.ConnectionString); await db.OpenAsync();
        var actor = await db.ExecuteScalarAsync<long>("SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'");
        var entered = await db.QuerySingleAsync<AnchorageEventResult>("""
            EXECUTE AS USER='portqueue_probe';
            EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_ENTER',@at,@actor,@return_details=1;
            REVERT;
            """, new { ap = seed.Appointment, a = seed.Anchorage, at = DateTimeOffset.Parse(Time(-60), CultureInfo.InvariantCulture).UtcDateTime, actor });
        var estimate = await db.QuerySingleAsync<AnchorageEstimateResult>("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_set_anchorage_estimate @id,@at,0,N'最小权限预计',@actor; REVERT;",
            new { id = entered.QueueEntryId, at = DateTimeOffset.Parse(Time(120), CultureInfo.InvariantCulture).UtcDateTime, actor });
        Assert.Equal("1", estimate.EstimateVersion);
        var left = await db.QuerySingleAsync<AnchorageEventResult>("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_record_anchorage_event NULL,NULL,'ANCHORAGE_LEAVE',@at,@actor,@queue_entry_id=@id,@return_details=1; REVERT;",
            new { id = entered.QueueEntryId, at = DateTimeOffset.Parse(Time(-30), CultureInfo.InvariantCulture).UtcDateTime, actor });
        Assert.Equal(entered.QueueEntryId, left.QueueEntryId);
        using var stream = typeof(PortQueue.Infrastructure.Anchorages.SqlAnchorageQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.AnchorageEntryCandidates.sql")!;
        using var reader = new StreamReader(stream);
        var result = await db.QuerySingleAsync<(long Total, string Items)>("EXECUTE AS USER='portqueue_probe'; " + await reader.ReadToEndAsync() + " REVERT;", new { Keyword = "", Offset = 0, PageSize = 20 });
        Assert.True(result.Total > 0);
        var direct = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXECUTE AS USER='portqueue_probe'; UPDATE dbo.anchorage_queue_entry SET manual_estimated_berthing_at=NULL; REVERT;"));
        Assert.Equal(229, direct.Number);
    }

    [Fact]
    public async Task ManualEstimateAndLegacyValueSurvivePlanConfirmation()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, "DISPATCHER"); using var loggedIn = client;
        var seed = await Seed(); var entered = await Enter(client, seed);
        using var saved = await Send(client, $"/api/anchorage-stays/{entered.QueueEntryId}/estimate",
            new { manualEstimatedBerthingAt = Time(120), expectedVersion = "0", reason = "人工调度预计" }, "PUT");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await using var db = new SqlConnection(fixture.ConnectionString);
        await db.ExecuteAsync("""
            UPDATE dbo.anchorage_queue_entry SET estimated_berthing_at='2035-01-01' WHERE queue_entry_id=@id;
            DECLARE @berth bigint=(SELECT MIN(b.berth_id) FROM dbo.berth b JOIN dbo.berth_ship_type st ON st.berth_id=b.berth_id
                JOIN dbo.ship s ON s.ship_type_item_id=st.ship_type_item_id WHERE s.ship_id=@ship AND st.relation_status='ENABLED' AND b.status_code='AVAILABLE');
            EXEC dbo.usp_create_berth_plan_revision @ap,@berth,@start,@finish,N'预计与计划独立测试',@actor;
            DECLARE @plan bigint=(SELECT berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap),@revision bigint;
            SELECT @revision=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan AND is_current=1;
            EXEC dbo.usp_confirm_berth_plan @plan,@revision,@actor;
            """, new { id = entered.QueueEntryId, ap = seed.Appointment, ship = seed.Ship, actor,
                start = DateTimeOffset.Parse(Time(180), CultureInfo.InvariantCulture).UtcDateTime, finish = DateTimeOffset.Parse(Time(360), CultureInfo.InvariantCulture).UtcDateTime });
        var detail = (await client.GetFromJsonAsync<AnchorageStayDetail>($"/api/anchorage-stays/{entered.QueueEntryId}"))!;
        Assert.Equal(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero), detail.LegacyEstimatedBerthingAt);
        Assert.NotNull(detail.ManualEstimatedBerthingAt); Assert.NotEqual(detail.ManualEstimatedBerthingAt, detail.ConfirmedBerthingAt);
        Assert.Equal("1", detail.EstimateVersion); Assert.Equal("ASSIGNED", detail.StatusCode);
    }

    [Fact]
    public async Task MigrationAddsEmptyIndependentFieldsAndIsRepeatableWithoutChangingHistory()
    {
        await using var isolated = new AsyncFixture(); await isolated.Database.InitializeAsync();
        await using var db = new SqlConnection(isolated.Database.ConnectionString);
        await db.ExecuteAsync("""
            ALTER TABLE dbo.anchorage_queue_entry DROP CONSTRAINT DF_queue_estimate_version,CK_queue_estimate_version;
            ALTER TABLE dbo.anchorage_queue_entry DROP COLUMN manual_estimated_berthing_at,estimate_version;
            UPDATE dbo.anchorage_queue_entry SET estimated_berthing_at='2034-06-01';
            """);
        var before = await db.QuerySingleAsync<(int Events, int Queue, int Audit)>("SELECT (SELECT COUNT(*) FROM dbo.port_event),(SELECT COUNT(*) FROM dbo.anchorage_queue_entry),(SELECT COUNT(*) FROM dbo.audit_log)");
        await isolated.Database.ApplyScriptAsync("16_anchorage_operations.sql");
        await isolated.Database.ApplyScriptAsync("16_anchorage_operations.sql");
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.anchorage_queue_entry WHERE manual_estimated_berthing_at IS NOT NULL OR estimate_version<>0 OR estimated_berthing_at<>'2034-06-01'"));
        Assert.Equal(before, await db.QuerySingleAsync<(int Events, int Queue, int Audit)>("SELECT (SELECT COUNT(*) FROM dbo.port_event),(SELECT COUNT(*) FROM dbo.anchorage_queue_entry),(SELECT COUNT(*) FROM dbo.audit_log)"));
    }

    private sealed class AsyncFixture : IAsyncDisposable
    {
        public AppointmentDatabaseFixture Database { get; } = new();
        public ValueTask DisposeAsync() => new(Database.DisposeAsync());
    }

    private static string Time(int minutes) => DateTimeOffset.UtcNow.AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string method = "POST", bool csrf = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return await client.SendAsync(request);
    }
    private static async Task<AnchorageEventResult> Enter(HttpClient client, TestSeed seed, string? time = null)
    {
        using var response = await Send(client, $"/api/anchorages/{seed.Anchorage}/entries", new { appointmentId = seed.Appointment.ToString(CultureInfo.InvariantCulture), eventTime = time ?? Time(-60) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AnchorageEventResult>())!;
    }
    private async Task<TestSeed> Seed(int capacity = 10, long? shipId = null)
    {
        await using var db = new SqlConnection(fixture.ConnectionString);
        return await db.QuerySingleAsync<TestSeed>("""
            SET QUOTED_IDENTIFIER ON;
            DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS'),@ship bigint=@shipId,
                @actor bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
            INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
            VALUES(@port,@tag,N'事件测试锚地','OUTER',@capacity,'AVAILABLE');
            DECLARE @a bigint=SCOPE_IDENTITY();
            IF @ship IS NULL
            BEGIN
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
                SELECT TOP(1) LEFT(@tag,7),LEFT(@tag,9),@tag,ship_type_item_id,180,25,10,30000,shipping_company_id,'ACTIVE' FROM dbo.ship ORDER BY ship_id;
                SET @ship=SCOPE_IDENTITY();
            END;
            INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
            VALUES(@tag,@ship,DATEADD(day,-1,SYSUTCDATETIME()),DATEADD(day,1,SYSUTCDATETIME()),'PLANNED');
            DECLARE @call bigint=SCOPE_IDENTITY();
            INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,
                declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at)
            VALUES(@tag,@call,DATEADD(day,-1,SYSUTCDATETIME()),DATEADD(day,1,SYSUTCDATETIME()),3,8,1,'APPROVED',DATEADD(day,-3,SYSUTCDATETIME()),@actor,DATEADD(day,-2,SYSUTCDATETIME()));
            SELECT CONVERT(bigint,SCOPE_IDENTITY()) AS Appointment,@a AS Anchorage,@ship AS Ship;
            """, new { tag = Guid.NewGuid().ToString("N")[..25], capacity, shipId });
    }
    private sealed record TestSeed(long Appointment, long Anchorage, long Ship);
    private static async Task<(HttpClient Client, long Actor)> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope(); var username = "events." + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), password));
        var id = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync("create", username, "锚地操作测试", role, hash, CancellationToken.None);
        using var response = await Send(client, "/api/session/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, id);
    }
}
