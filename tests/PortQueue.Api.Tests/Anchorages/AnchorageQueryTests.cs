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
using PortQueue.Infrastructure.Anchorages;

namespace PortQueue.Api.Tests.Anchorages;

[Trait("Category", "Database")]
public sealed class AnchorageQueryTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Theory]
    [InlineData("/api/anchorages")]
    [InlineData("/api/anchorages/1/queue")]
    [InlineData("/api/anchorage-stays/1")]
    public async Task AnonymousCannotReadOperationalQueue(string endpoint)
    {
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("DISPATCHER")]
    [InlineData("REVIEWER")]
    [InlineData("RECORDER")]
    public async Task EveryEnabledRoleCanReadListQueueAndStay(string role)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, role);
        using var connection = new SqlConnection(fixture.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Anchorage, long Stay)>(
            "SELECT TOP(1) anchorage_id,queue_entry_id FROM dbo.v_anchorage_queue ORDER BY queue_entry_id");
        foreach (var path in new[] { "/api/anchorages", $"/api/anchorages/{ids.Anchorage}/queue", $"/api/anchorage-stays/{ids.Stay}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task FullQueuePreservesFactsHistoryScopeAndSeparateEstimateSources()
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "DISPATCHER");
        using var list = await GetJson(client, $"/api/anchorages?appointmentId={scenario.Appointment}");
        var root = list.RootElement;
        Assert.Equal("SH-YS", root.GetProperty("portCode").GetString());
        Assert.Equal(scenario.Current.ToString(System.Globalization.CultureInfo.InvariantCulture), root.GetProperty("locatedStay").GetProperty("queueEntryId").GetString());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("observedAt").GetDateTimeOffset().Offset);
        var anchorages = root.GetProperty("items").EnumerateArray().ToArray();
        var active = Assert.Single(anchorages.Where(a => a.GetProperty("anchorageId").GetString() == scenario.Anchorage.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(8, active.GetProperty("occupiedCount").GetInt64());
        Assert.Equal(7, active.GetProperty("queueCount").GetInt64());
        Assert.Equal(20, active.GetProperty("capacity").GetInt32());
        var empty = Assert.Single(anchorages.Where(a => a.GetProperty("anchorageId").GetString() == scenario.Empty.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("capacity").ValueKind);
        Assert.Equal(0, empty.GetProperty("occupiedCount").GetInt64());
        Assert.Equal("RESTRICTED", empty.GetProperty("statusCode").GetString());
        Assert.DoesNotContain(anchorages, a => a.GetProperty("anchorageId").GetString() == scenario.Foreign.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var queue = await GetJson(client, $"/api/anchorages/{scenario.Anchorage}/queue");
        Assert.Equal(8, queue.RootElement.GetProperty("occupiedCount").GetInt32());
        Assert.Equal(20, queue.RootElement.GetProperty("capacity").GetInt32());
        Assert.Equal("AVAILABLE", queue.RootElement.GetProperty("statusCode").GetString());
        var items = queue.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(7, items.Length);
        Assert.Equal(Enumerable.Range(1, 7), items.Select(i => i.GetProperty("queuePosition").GetInt32()));
        var cancelled = Assert.Single(queue.RootElement.GetProperty("cancelled").EnumerateArray());
        Assert.Equal("CANCELLED", cancelled.GetProperty("statusCode").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("queuePosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("leftAt").ValueKind);
        Assert.Equal(64, queue.RootElement.GetProperty("snapshot").GetString()!.Length);

        using var detail = await GetJson(client, $"/api/anchorage-stays/{scenario.Current}");
        var stay = detail.RootElement;
        // 确认动作发生在19日，计划靠泊在20日；二者不得混成一个已完成事实。
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), stay.GetProperty("planConfirmedAt").GetDateTimeOffset());
        Assert.Equal(scenario.Current.ToString(System.Globalization.CultureInfo.InvariantCulture), stay.GetProperty("queueEntryId").GetString());
        Assert.Equal(scenario.Appointment.ToString(System.Globalization.CultureInfo.InvariantCulture), stay.GetProperty("appointmentId").GetString());
        Assert.True(scenario.Appointment > 9_007_199_254_740_991L);
        Assert.Equal(JsonValueKind.Null, stay.GetProperty("manualEstimatedBerthingAt").ValueKind);
        Assert.Equal("0", stay.GetProperty("estimateVersion").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), stay.GetProperty("legacyEstimatedBerthingAt").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 13, 0, 0, TimeSpan.Zero), stay.GetProperty("confirmedBerthingAt").GetDateTimeOffset());
        Assert.NotEmpty(stay.GetProperty("callNo").GetString()!);
        Assert.NotEmpty(stay.GetProperty("reviewerName").GetString()!);
        Assert.Equal(3, stay.GetProperty("events").GetArrayLength());
        Assert.Equal(2, stay.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("queueEntryId").GetString()).Distinct().Count());
        using var history = await GetJson(client, $"/api/anchorage-stays/{scenario.History}");
        Assert.Equal("LEFT", history.RootElement.GetProperty("statusCode").GetString());
        Assert.NotEqual(JsonValueKind.Null, history.RootElement.GetProperty("leftAt").ValueKind);
        using var emptyQueue = await GetJson(client, $"/api/anchorages/{scenario.Empty}/queue");
        Assert.Equal(0, emptyQueue.RootElement.GetProperty("occupiedCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, emptyQueue.RootElement.GetProperty("capacity").ValueKind);
        Assert.Equal("RESTRICTED", emptyQueue.RootElement.GetProperty("statusCode").GetString());
        Assert.Empty(emptyQueue.RootElement.GetProperty("items").EnumerateArray());
        Assert.Empty(emptyQueue.RootElement.GetProperty("cancelled").EnumerateArray());
        using var outside = await client.GetAsync($"/api/anchorages/{scenario.Foreign}/queue");
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
    }

    [Fact]
    public async Task InvalidAndMissingIdentifiersAreRejected()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "REVIEWER");
        foreach (var path in new[] { "/api/anchorages/0/queue", "/api/anchorages?appointmentId=-1", "/api/anchorage-stays/abc", "/api/anchorage-stays/9223372036854775808" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var path in new[] { "/api/anchorages/9223372036854775807/queue", "/api/anchorage-stays/9223372036854775807" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static async Task<JsonDocument> GetJson(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MinimumPermissionUserCanRunAllQueriesAndSnapshotMatchesSql()
    {
        var scenario = await SeedScenario();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var query in new[] { "Anchorages", "AnchorageQueue", "AnchorageStay" })
        {
            using var stream = typeof(SqlAnchorageQueries).Assembly.GetManifestResourceStream($"PortQueue.Queries.{query}.sql")!;
            using var reader = new StreamReader(stream);
            var payload = await connection.QuerySingleAsync<string>("EXECUTE AS USER='portqueue_probe'; " + await reader.ReadToEndAsync() + " REVERT;",
                new { HomePortCode = "SH-YS", AppointmentId = scenario.Appointment, AnchorageId = scenario.Anchorage, QueueEntryId = scenario.Current });
            Assert.NotEmpty(payload);
            using var parsed = JsonDocument.Parse(payload);
            Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        }
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var scope = factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAnchorageQueries>();
        var queue = (await queries.GetQueueAsync(scenario.Anchorage, CancellationToken.None))!;
        var sqlSnapshot = await connection.QuerySingleAsync<string>("""
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE(STRING_AGG(
                CONVERT(nvarchar(max),queue_entry_id)+N':'+CONVERT(nvarchar(20),queue_position)+N':'+queue_status_code,N'|')
                WITHIN GROUP(ORDER BY queue_position,queue_entry_id),N'')),2)
            FROM dbo.v_anchorage_queue WHERE anchorage_id=@id AND queue_status_code IN ('WAITING','ASSIGNED');
            """, new { id = scenario.Anchorage });
        Assert.Equal(sqlSnapshot, queue.Snapshot);
        var empty = (await queries.GetQueueAsync(scenario.Empty, CancellationToken.None))!;
        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", empty.Snapshot);
        await connection.ExecuteAsync("UPDATE dbo.anchorage_queue_entry SET queue_status_code='ASSIGNED' WHERE queue_entry_id=@id", new { id = scenario.Current });
        Assert.NotEqual(queue.Snapshot, (await queries.GetQueueAsync(scenario.Anchorage, CancellationToken.None))!.Snapshot);
        var denied = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("EXECUTE AS USER='portqueue_probe'; UPDATE dbo.anchorage SET capacity_vessel_count=99; REVERT;"));
        Assert.Equal(229, denied.Number);
    }

    [Theory]
    [InlineData("DRAFT", false)]
    [InlineData("CANCELLED", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("CONFIRMED", true)]
    public async Task InapplicableOrUnconfirmedPlansNeverAppearAsConfirmed(string status, bool clearConfirmation)
    {
        var scenario = await SeedScenario();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync("""
            UPDATE dbo.berth_plan SET plan_status_code=@status WHERE appointment_id=@appointment;
            IF @clearConfirmation=1
                UPDATE r SET confirmed_by=NULL,confirmed_at=NULL FROM dbo.berth_plan_revision r
                JOIN dbo.berth_plan p ON p.berth_plan_id=r.berth_plan_id WHERE p.appointment_id=@appointment;
            """, new { status, clearConfirmation, appointment = scenario.Appointment });
        using var scope = fixture.Factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAnchorageQueries>();
        var detail = (await queries.GetStayAsync(scenario.Current, CancellationToken.None))!;
        Assert.Null(detail.ConfirmedBerthingAt);
        Assert.Null(detail.ConfirmedRevisionId);
        Assert.Null(detail.BerthName);
        Assert.NotNull(detail.LegacyEstimatedBerthingAt);
        var member = Assert.Single((await queries.GetQueueAsync(scenario.Anchorage, CancellationToken.None))!.Items
            .Where(q => q.QueueEntryId == detail.QueueEntryId));
        Assert.Null(member.ConfirmedBerthingAt);
    }

    [Fact]
    public async Task ClosedStayKeepsItsLastConfirmedPlanWhenLaterRevisionsExist()
    {
        var scenario = await SeedScenario();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync("""
            DECLARE @plan bigint=(SELECT berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@appointment);
            UPDATE dbo.berth_plan_revision SET is_current=0 WHERE berth_plan_id=@plan;
            INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                revision_reason,revised_by,confirmed_by,confirmed_at,is_current)
            SELECT berth_plan_id,2,berth_id,'2026-11-21T13:00:00','2026-11-21T16:00:00',N'后续安排',revised_by,confirmed_by,'2026-11-20T02:30:00',1
            FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan AND version_no=1;
            UPDATE dbo.berth_plan SET plan_status_code='COMPLETED' WHERE berth_plan_id=@plan;
            """, new { appointment = scenario.Appointment });
        using var scope = fixture.Factory.Services.CreateScope();
        var detail = (await scope.ServiceProvider.GetRequiredService<IAnchorageQueries>().GetStayAsync(scenario.History, CancellationToken.None))!;
        Assert.Equal(new DateTimeOffset(2026,11,20,13,0,0,TimeSpan.Zero), detail.ConfirmedBerthingAt);
    }

    [Fact]
    public async Task LatestClosedStayRemainsLocatableAndSameShipVisitsDoNotShareEvents()
    {
        var scenario = await SeedScenario();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync("""
            DECLARE @operator bigint=(SELECT MIN(user_id) FROM dbo.user_account WHERE status_code='ENABLED');
            EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_LEAVE','2026-11-20T03:00:00',@operator,@queue_entry_id=@stay;
            UPDATE dbo.anchorage SET capacity_vessel_count=0,status_code='DISABLED' WHERE anchorage_id=@empty;
            """, new { appointment = scenario.Appointment, anchorage = scenario.Anchorage, empty = scenario.Empty, stay = scenario.Current });
        using var scope = fixture.Factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAnchorageQueries>();
        var list = (await queries.GetAsync(scenario.Appointment, CancellationToken.None))!;
        Assert.Equal(scenario.Current.ToString(System.Globalization.CultureInfo.InvariantCulture), list.LocatedStay!.QueueEntryId);
        var empty = Assert.Single(list.Items.Where(a => a.AnchorageId == scenario.Empty.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(0, empty.Capacity);
        Assert.Equal("DISABLED", empty.StatusCode);
        var queue = (await queries.GetQueueAsync(scenario.Anchorage, CancellationToken.None))!;
        Assert.Equal(6, queue.Items.Count);
        Assert.Equal(7, queue.OccupiedCount);
        var emptyQueue = (await queries.GetQueueAsync(scenario.Empty, CancellationToken.None))!;
        Assert.Equal(0, emptyQueue.OccupiedCount);
        Assert.Equal(0, emptyQueue.Capacity);
        Assert.Equal("DISABLED", emptyQueue.StatusCode);
        var current = (await queries.GetStayAsync(scenario.Current, CancellationToken.None))!;
        // 同一物理船的下一次到访必须发生在前次离锚后，不能靠重叠的非法夹具证明区分。
        await connection.ExecuteAsync("""
            DECLARE @operator bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
            INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
            VALUES(@callNo+'-next',@ship,'2026-11-20','2026-11-21','PLANNED');
            DECLARE @call bigint=SCOPE_IDENTITY();
            INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,
                declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at)
            VALUES(@callNo+'-next',@call,'2026-11-20','2026-11-21',3,8,1,'APPROVED','2026-11-18',@operator,'2026-11-19');
            DECLARE @appointment bigint=SCOPE_IDENTITY();
            EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_ENTER','2026-11-20T04:00:00',@operator;
            """, new { callNo = current.CallNo, ship = current.ShipId, anchorage = scenario.Anchorage });
        var otherId = await connection.ExecuteScalarAsync<long>("SELECT queue_entry_id FROM dbo.v_anchorage_queue WHERE ship_id=@ship AND left_anchorage_at IS NULL", new { ship = current.ShipId });
        var other = (await queries.GetStayAsync(otherId, CancellationToken.None))!;
        Assert.Equal(current.ShipId, other.ShipId);
        Assert.NotEqual(current.PortCallId, other.PortCallId);
        Assert.Equal(4, current.Events.Count);
        Assert.Single(other.Events);
        Assert.All(other.Events, e => Assert.Equal(other.QueueEntryId, e.QueueEntryId));
        var missing = (await queries.GetAsync(long.MaxValue, CancellationToken.None))!;
        Assert.Null(missing.LocatedStay);
        Assert.Equal(list.Items.Count, missing.Items.Count);
    }

    private async Task<Scenario> SeedScenario()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        var prefix = "Q" + Guid.NewGuid().ToString("N")[..12];
        // Synthetic fixtures live only in this random test database. Events still use the real transaction.
        await connection.ExecuteAsync("""
            SET QUOTED_IDENTIFIER ON;
            DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS'),
                @operator bigint=(SELECT MIN(user_id) FROM dbo.user_account WHERE status_code='ENABLED');
            INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
            VALUES(@port,@prefix,N'长队列验证','GENERAL',20,'AVAILABLE'),(@port,@prefix+'E',N'空锚地验证','GENERAL',NULL,'RESTRICTED');
            DECLARE @anchorage bigint=(SELECT anchorage_id FROM dbo.anchorage WHERE anchorage_code=@prefix),@foreign bigint;
            INSERT dbo.port(port_code,port_name,country_code,timezone,status_code)
            VALUES(RIGHT(@prefix,10),N'非主港','CN','Asia/Shanghai','ENABLED');
            SET @foreign=SCOPE_IDENTITY();
            INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
            VALUES(@foreign,@prefix+'F',N'非主港锚地','GENERAL',0,'DISABLED');
            DECLARE @i int=1,@call bigint,@appointment bigint,@first bigint,@at datetime2(0),@ship bigint,@first_stay bigint;
            WHILE @i<=8
            BEGIN
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,shipping_company_id,status_code)
                SELECT TOP(1) CONCAT(RIGHT(@prefix,6),@i),CONCAT(RIGHT(@prefix,8),@i),CONCAT(N'排队测试船',@prefix,@i),
                    ship_type_item_id,180,25,10,shipping_company_id,'ACTIVE' FROM dbo.ship ORDER BY ship_id;
                SET @ship=SCOPE_IDENTITY();
                INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
                VALUES(CONCAT(@prefix,'-',@i),@ship,'2026-11-20','2026-11-21','IN_PROGRESS');
                SET @call=SCOPE_IDENTITY();
                INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,
                    estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at)
                VALUES(CONCAT(@prefix,'-',@i),@call,'2026-11-20','2026-11-21',3,8,1,'APPROVED','2026-11-18',@operator,'2026-11-19');
                SET @appointment=SCOPE_IDENTITY();
                IF @i=1 SET @first=@appointment;
                SET @at=DATEADD(minute,@i,CONVERT(datetime2(0),'2026-11-20'));
                EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_ENTER',@at,@operator;
                IF @i=8
                BEGIN
                    UPDATE dbo.arrival_appointment SET appointment_status_code='CANCELLED' WHERE appointment_id=@appointment;
                    UPDATE dbo.anchorage_queue_entry SET queue_status_code='CANCELLED',queue_position=NULL WHERE appointment_id=@appointment;
                END;
                SET @i+=1;
            END;
            SELECT @first_stay=queue_entry_id FROM dbo.v_anchorage_queue WHERE appointment_id=@first AND left_anchorage_at IS NULL;
            EXEC dbo.usp_record_anchorage_event @first,@anchorage,'ANCHORAGE_LEAVE','2026-11-20T01:00:00',@operator,@queue_entry_id=@first_stay;
            EXEC dbo.usp_record_anchorage_event @first,@anchorage,'ANCHORAGE_ENTER','2026-11-20T02:00:00',@operator;
            UPDATE dbo.anchorage_queue_entry SET estimated_berthing_at='2026-11-20T12:00:00' WHERE appointment_id=@first AND queue_status_code='WAITING';
            DECLARE @berth bigint=(SELECT MIN(berth_id) FROM dbo.berth),@plan bigint;
            INSERT dbo.berth_plan(appointment_id,plan_status_code) VALUES(@first,'CONFIRMED');
            SET @plan=SCOPE_IDENTITY();
            INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                revision_reason,revised_by,confirmed_by,confirmed_at,is_current)
            VALUES(@plan,1,@berth,'2026-11-20T13:00:00','2026-11-20T16:00:00',N'测试已确认计划',@operator,@operator,'2026-11-19',1);
            """, new { prefix });
        return await connection.QuerySingleAsync<Scenario>("""
            SELECT a.anchorage_id Anchorage,
                (SELECT anchorage_id FROM dbo.anchorage WHERE anchorage_code=@prefix+'E') Empty,
                (SELECT anchorage_id FROM dbo.anchorage WHERE anchorage_code=@prefix+'F') [Foreign],
                aa.appointment_id Appointment,
                (SELECT queue_entry_id FROM dbo.anchorage_queue_entry WHERE appointment_id=aa.appointment_id AND queue_status_code='WAITING') [Current],
                (SELECT queue_entry_id FROM dbo.anchorage_queue_entry WHERE appointment_id=aa.appointment_id AND queue_status_code='LEFT') History
            FROM dbo.anchorage a JOIN dbo.arrival_appointment aa ON aa.appointment_no=@prefix+'-1'
            WHERE a.anchorage_code=@prefix;
            """, new { prefix });
    }

    private sealed record Scenario(long Anchorage, long Empty, long Foreign, long Appointment, long Current, long History);

    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var username = "anchorage." + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>()
            .HashPassword(new Account(), password));
        await scope.ServiceProvider.GetRequiredService<IAccountStore>()
            .ManageAsync("create", username, "锚地测试", role, hash, CancellationToken.None);
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login")
        { Content = JsonContent.Create(new { username, password }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
