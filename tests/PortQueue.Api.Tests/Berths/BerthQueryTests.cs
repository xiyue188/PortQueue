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
using PortQueue.Application.Berths;
using PortQueue.Infrastructure.Berths;

namespace PortQueue.Api.Tests.Berths;

[Trait("Category", "Database")]
public sealed class BerthQueryTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private static readonly int[] VisibleVersions = [2, 4];
    private static readonly int[] AllVersions = [1, 2, 3, 4];
    [Theory]
    [InlineData("/api/berths/schedule?from=2026-11-20T00:00:00Z&to=2026-11-21T00:00:00Z")]
    [InlineData("/api/berths/visits")]
    [InlineData("/api/berths/visits/1")]
    public async Task AnonymousCannotReadBerthWorkspace(string endpoint)
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
    public async Task AllFourRolesReadScheduleVisitsAndDetail(string role)
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, role);
        foreach (var endpoint in new[] { ScheduleUrl(), "/api/berths/visits", $"/api/berths/visits/{scenario.First}" })
        {
            using var response = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/api/berths/schedule")]
    [InlineData("/api/berths/schedule?from=2026-11-20&to=2026-11-21")]
    [InlineData("/api/berths/schedule?from=2026-11-20T00:00:00&to=2026-11-21T00:00:00Z")]
    [InlineData("/api/berths/schedule?from=2026-11-20T00:00:00Z&to=2026-11-20T00:00:00Z")]
    [InlineData("/api/berths/schedule?from=2026-11-20T00:00:00Z&to=2026-11-19T00:00:00Z")]
    [InlineData("/api/berths/schedule?from=2026-11-20T00:00:00Z&to=2026-11-22T00:00:01Z")]
    [InlineData("/api/berths/visits?scope=other")]
    [InlineData("/api/berths/visits?page=0")]
    [InlineData("/api/berths/visits?pageSize=101")]
    [InlineData("/api/berths/visits/0")]
    [InlineData("/api/berths/visits/9223372036854775808")]
    public async Task InvalidQueriesReturnBadRequest(string endpoint)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "RECORDER");
        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WaitingIsPagedBeyondThreeAndSearchTreatsSpecialCharactersLiterally()
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "DISPATCHER");
        var first = await Get<BerthVisitPage>(client, $"/api/berths/visits?keyword={scenario.Prefix}&pageSize=8");
        var second = await Get<BerthVisitPage>(client, $"/api/berths/visits?keyword={scenario.Prefix}&pageSize=8&page=2");
        Assert.Equal(10, first.TotalCount);
        Assert.Equal(8, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(10, first.Items.Concat(second.Items).Select(v => v.AppointmentId).Distinct().Count());
        Assert.Null(first.Items[0].QueueEntryId); // 直进到访仍是待安排。
        Assert.Contains(first.Items, v => v.PlanStatusCode == "DRAFT");
        Assert.All(first.Items, v => Assert.True(long.Parse(v.AppointmentId, System.Globalization.CultureInfo.InvariantCulture) > 9_007_199_254_740_991));
        var literal = await Get<BerthVisitPage>(client,
            "/api/berths/visits?keyword=" + Uri.EscapeDataString(scenario.Prefix + "-%_[\\"));
        Assert.Single(literal.Items);
        Assert.Equal(1, literal.TotalCount);
        var all = await Get<BerthVisitPage>(client, $"/api/berths/visits?scope=all&keyword={scenario.Prefix}&pageSize=100");
        Assert.Equal(15, all.TotalCount);
        Assert.Contains(all.Items, v => v.PlanStatusCode == "CANCELLED");
        Assert.Contains(all.Items, v => v.PlanStatusCode == "COMPLETED");
        Assert.DoesNotContain(all.Items, v => v.AppointmentId == Id(scenario.ForeignAppointment));
        var missing = await Get<BerthVisitPage>(client, $"/api/berths/visits?keyword={scenario.Prefix}&page=100");
        Assert.Empty(missing.Items);
        Assert.Equal(10, missing.TotalCount);
    }

    [Fact]
    public async Task ScheduleKeepsOnlyCurrentAndLatestCandidateAndDoesNotReleaseActualOccupancy()
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "RECORDER");
        var schedule = await Get<BerthSchedule>(client, ScheduleUrl());
        Assert.Equal(TimeSpan.Zero, schedule.ObservedAt.Offset);
        var resource = Assert.Single(schedule.Resources.Where(r => r.BerthId == Id(scenario.Berth)));
        Assert.NotEmpty(resource.ShipTypes);
        var empty = Assert.Single(schedule.Resources.Where(r => r.BerthId == Id(scenario.EmptyBerth)));
        Assert.Equal("DISABLED", empty.StatusCode);
        Assert.Null(empty.MaxDwt);
        Assert.DoesNotContain(schedule.Resources, r => r.BerthId == Id(scenario.ForeignBerth));
        var versions = schedule.Revisions.Where(r => r.AppointmentId == Id(scenario.Confirmed)).ToArray();
        Assert.Equal(VisibleVersions, versions.Select(v => v.VersionNo).Order().ToArray());
        Assert.True(Assert.Single(versions.Where(v => v.VersionNo == 2)).IsCurrent);
        Assert.Null(Assert.Single(versions.Where(v => v.VersionNo == 4)).ConfirmedAt);
        Assert.DoesNotContain(schedule.Revisions, v => v.AppointmentId == Id(scenario.InvalidConfirmed));
        Assert.DoesNotContain(schedule.Revisions, v => v.PlanStatusCode == "CANCELLED");
        var draft = Assert.Single(schedule.Revisions.Where(v => v.AppointmentId == Id(scenario.Draft)));
        Assert.Equal("DRAFT", draft.PlanStatusCode);
        Assert.Null(draft.ConfirmedAt);
        var occupied = Assert.Single(schedule.Occupancies.Where(o => o.AppointmentId == Id(scenario.Executing)));
        Assert.Equal(Id(scenario.ExecutedRevision), occupied.ExecutedRevisionId);
        Assert.Equal(Id(scenario.Berth), occupied.BerthId);
        Assert.Equal(Utc("2026-11-18T01:00:00Z"), occupied.ActualBerthingAt);
        Assert.Null(occupied.ActualUnberthingAt);
        Assert.Contains(schedule.Occupancies, o => o.AppointmentId == Id(scenario.Completed));
        var future = await Get<BerthSchedule>(client, ScheduleUrl("2027-01-01T00:00:00Z", "2027-01-02T00:00:00Z"));
        Assert.Contains(future.Occupancies, o => o.AppointmentId == Id(scenario.Executing));
        Assert.DoesNotContain(future.Occupancies, o => o.AppointmentId == Id(scenario.Completed));
        var adjoining = await Get<BerthSchedule>(client, ScheduleUrl("2026-11-20T03:00:00Z", "2026-11-20T06:00:00Z"));
        Assert.DoesNotContain(adjoining.Occupancies, o => o.AppointmentId == Id(scenario.Completed));
        Assert.DoesNotContain(adjoining.Revisions, v => v.AppointmentId == Id(scenario.Confirmed));
        var offset = await Get<BerthSchedule>(client, ScheduleUrl("2026-11-20T08:00:00+08:00", "2026-11-22T08:00:00+08:00"));
        Assert.Equal(Utc("2026-11-20T00:00:00Z"), offset.From);
        Assert.Equal(Utc("2026-11-22T00:00:00Z"), offset.To);
    }

    [Fact]
    public async Task HistoricalScheduleKeepsCompletedPlanButNeverBackdatesActualOccupancy()
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "RECORDER");
        var history = await Get<BerthSchedule>(client, ScheduleUrl("2026-11-18T00:00:00Z", "2026-11-19T00:00:00Z"));
        var completed = Assert.Single(history.Revisions.Where(v => v.AppointmentId == Id(scenario.Completed)));
        Assert.Equal("COMPLETED", completed.PlanStatusCode);
        Assert.True(completed.IsCurrent);
        Assert.NotNull(completed.ConfirmedAt);
        var beforeBerthing = await Get<BerthSchedule>(client, ScheduleUrl("2026-11-17T00:00:00Z", "2026-11-18T01:00:00Z"));
        Assert.DoesNotContain(beforeBerthing.Occupancies, o => o.AppointmentId == Id(scenario.Executing));
        Assert.DoesNotContain(beforeBerthing.Occupancies, o => o.AppointmentId == Id(scenario.Completed));
    }

    [Fact]
    public async Task DetailPreservesAllVersionsExecutionBaselineAndLatestHomeAnchorageStay()
    {
        var scenario = await SeedScenario();
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        using var client = await Login(factory, "REVIEWER");
        var confirmed = await Get<BerthVisitDetail>(client, $"/api/berths/visits/{scenario.Confirmed}");
        Assert.Equal(AllVersions, confirmed.Versions.Select(v => v.VersionNo).ToArray());
        var executed = await Get<BerthVisitDetail>(client, $"/api/berths/visits/{scenario.Executing}");
        Assert.Equal(2, executed.Versions.Count);
        Assert.Equal(2, executed.Events.Count);
        Assert.Equal(Id(scenario.ExecutedRevision), executed.Events.Single(e => e.EventTypeCode == "BERTHING").ExecutedRevisionId);
        Assert.NotEqual(Id(scenario.ExecutedRevision), executed.Versions.Single(v => v.IsCurrent).RevisionId);
        var stay = await Get<BerthVisitDetail>(client, $"/api/berths/visits/{scenario.WithStay}");
        Assert.Equal(Id(scenario.CurrentStay), stay.QueueEntryId);
        Assert.Equal(Utc("2026-11-18T03:00:00Z"), stay.EnteredAt);
        Assert.Null(stay.LeftAt);
        Assert.Equal(Utc("2026-11-20T09:00:00Z"), stay.ManualEstimatedBerthingAt);
        Assert.Equal(3, stay.Events.Count);
        Assert.NotNull(stay.SubmittedAt);
        Assert.NotNull(stay.ReviewedAt);
        Assert.NotNull(stay.ReviewerName);
        Assert.Equal(stay.Events.OrderBy(e => e.EventTime).Select(e => e.EventId), stay.Events.Select(e => e.EventId));
        foreach (var id in new[] { scenario.ForeignAppointment, long.MaxValue })
        {
            using var response = await client.GetAsync($"/api/berths/visits/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task MinimumColumnPermissionReadsAllQueriesButCannotWriteBaseTables()
    {
        var scenario = await SeedScenario();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var query in new[] { "BerthSchedule", "BerthVisits", "BerthVisit" })
        {
            using var stream = typeof(SqlBerthQueries).Assembly.GetManifestResourceStream($"PortQueue.Queries.{query}.sql")!;
            using var reader = new StreamReader(stream);
            var payload = await connection.QuerySingleAsync<string>("EXECUTE AS USER='portqueue_probe'; " + await reader.ReadToEndAsync() + " REVERT;",
                new { HomePortCode = "SH-YS", FromUtc = Utc("2026-11-20T00:00:00Z").UtcDateTime,
                    ToUtc = Utc("2026-11-21T00:00:00Z").UtcDateTime, AppointmentId = scenario.WithStay,
                    Scope = "all", Keyword = scenario.Prefix, Page = 1, PageSize = 8, Offset = 0L });
            using var parsed = JsonDocument.Parse(payload);
            Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        }
        var denied = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "EXECUTE AS USER='portqueue_probe'; UPDATE dbo.berth SET length_m=1000; REVERT;"));
        Assert.Equal(229, denied.Number);
    }

    private async Task<Scenario> SeedScenario()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        var prefix = "B" + Guid.NewGuid().ToString("N")[..12];
        // 独立随机库中的事实夹具，进港/靠离泊均走原事务过程；不触碰开发库。
        await connection.ExecuteAsync("""
            SET QUOTED_IDENTIFIER ON;
            DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS'),
                @operator bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'),
                @type bigint=(SELECT TOP(1) ship_type_item_id FROM dbo.ship ORDER BY ship_id),@terminal bigint,@berth bigint,@foreign_berth bigint;
            INSERT dbo.terminal(port_id,terminal_code,terminal_name,status_code) VALUES(@port,@prefix,N'泊位验证码头','AVAILABLE');
            SET @terminal=SCOPE_IDENTITY();
            INSERT dbo.berth(terminal_id,berth_code,berth_name,length_m,max_draft_m,max_dwt,status_code)
            VALUES(@terminal,@prefix,N'泊位验证',350,16,100000,'AVAILABLE'),(@terminal,@prefix+'E',N'停用空泊位',200,10,NULL,'DISABLED');
            SELECT @berth=berth_id FROM dbo.berth WHERE terminal_id=@terminal AND berth_code=@prefix;
            INSERT dbo.berth_ship_type(berth_id,ship_type_item_id,relation_status) VALUES(@berth,@type,'ENABLED');
            INSERT dbo.port(port_code,port_name,country_code,timezone,status_code) VALUES(RIGHT(@prefix,10),N'外港验证','CN','Asia/Shanghai','ENABLED');
            DECLARE @foreign bigint=SCOPE_IDENTITY();
            INSERT dbo.terminal(port_id,terminal_code,terminal_name,status_code) VALUES(@foreign,@prefix,N'外港码头','AVAILABLE');
            SET @terminal=SCOPE_IDENTITY();
            INSERT dbo.berth(terminal_id,berth_code,berth_name,length_m,max_draft_m,status_code) VALUES(@terminal,@prefix+'F',N'外港泊位',350,16,'AVAILABLE');
            SET @foreign_berth=SCOPE_IDENTITY();
            INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
            VALUES(@port,@prefix,N'泊位到访锚地','GENERAL',20,'AVAILABLE');
            DECLARE @anchorage bigint=SCOPE_IDENTITY(),@i int=1,@ship bigint,@call bigint,@appointment bigint,@plan bigint,@revision bigint,@stay bigint;
            WHILE @i<=16
            BEGIN
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
                SELECT RIGHT(@prefix,5)+RIGHT('0'+CONVERT(varchar(2),@i),2),RIGHT(@prefix,7)+RIGHT('0'+CONVERT(varchar(2),@i),2),
                    @prefix+CONVERT(varchar(2),@i),@type,180,25,10,30000,MIN(shipping_company_id),'ACTIVE' FROM dbo.ship;
                SET @ship=SCOPE_IDENTITY();
                INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
                VALUES(@prefix+'-'+CONVERT(varchar(2),@i),@ship,'2026-11-18','2026-11-22','PLANNED');
                SET @call=SCOPE_IDENTITY();
                INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,
                    estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at)
                VALUES(CASE WHEN @i=1 THEN @prefix+'-%_[\' ELSE @prefix+'-'+CONVERT(varchar(2),@i) END,@call,
                    DATEADD(minute,@i,CONVERT(datetime2(0),'2026-11-18')),'2026-11-22',4,8,1,'APPROVED','2026-11-16',@operator,'2026-11-17');
                SET @appointment=SCOPE_IDENTITY();
                IF @i=1 UPDATE dbo.ship SET status_code='DISABLED' WHERE ship_id=@ship;
                IF @i=2
                BEGIN
                    EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_ENTER','2026-11-18T01:00:00',@operator;
                    SELECT @stay=queue_entry_id FROM dbo.v_anchorage_queue WHERE appointment_id=@appointment AND left_anchorage_at IS NULL;
                    EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_LEAVE','2026-11-18T02:00:00',@operator,@queue_entry_id=@stay;
                    EXEC dbo.usp_record_anchorage_event @appointment,@anchorage,'ANCHORAGE_ENTER','2026-11-18T03:00:00',@operator;
                    UPDATE dbo.anchorage_queue_entry SET manual_estimated_berthing_at='2026-11-20T09:00:00',estimate_version=1
                        WHERE appointment_id=@appointment AND queue_status_code='WAITING';
                END;
                IF @i=3 OR @i>=11
                BEGIN
                    INSERT dbo.berth_plan(appointment_id,plan_status_code)
                    VALUES(@appointment,CASE WHEN @i=3 THEN 'DRAFT' WHEN @i=15 THEN 'CANCELLED' ELSE 'CONFIRMED' END);
                    SET @plan=SCOPE_IDENTITY();
                    INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                        revision_reason,revised_by,revised_at,confirmed_by,confirmed_at,is_current)
                    VALUES(@plan,1,CASE WHEN @i=14 THEN @foreign_berth ELSE @berth END,
                        CASE WHEN @i IN(12,13) THEN '2026-11-18T02:00:00' ELSE '2026-11-20T06:00:00' END,
                        CASE WHEN @i IN(12,13) THEN '2026-11-18T06:00:00' ELSE '2026-11-20T10:00:00' END,
                        N'原安排',@operator,'2026-11-17',CASE WHEN @i IN(3,16) THEN NULL ELSE @operator END,
                        CASE WHEN @i IN(3,16) THEN NULL ELSE '2026-11-17' END,1);
                    SET @revision=SCOPE_IDENTITY();
                    IF @i=11
                    BEGIN
                        UPDATE dbo.berth_plan_revision SET is_current=0 WHERE revision_id=@revision;
                        INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                            revision_reason,revised_by,revised_at,confirmed_by,confirmed_at,is_current)
                        VALUES(@plan,2,@berth,'2026-11-20T06:00:00','2026-11-20T10:00:00',N'生效调整',@operator,'2026-11-18',@operator,'2026-11-18',1),
                            (@plan,3,@berth,'2026-11-20T07:00:00','2026-11-20T11:00:00',N'旧候选',@operator,'2026-11-19',NULL,NULL,0),
                            (@plan,4,@berth,'2026-11-20T08:00:00','2026-11-20T12:00:00',N'最新候选',@operator,'2026-11-19T01:00:00',NULL,NULL,0);
                    END;
                    IF @i IN(12,13)
                    BEGIN
                        EXEC dbo.usp_record_port_entry @appointment,'2026-11-18T00:00:00',@operator;
                        EXEC dbo.usp_record_berth_event @appointment,'BERTHING','2026-11-18T01:00:00',@revision,@operator;
                        IF @i=12
                        BEGIN
                            UPDATE dbo.berth_plan_revision SET is_current=0 WHERE revision_id=@revision;
                            INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                                revision_reason,revised_by,revised_at,confirmed_by,confirmed_at,is_current)
                            VALUES(@plan,2,@berth,'2026-11-18T02:00:00','2026-11-19T03:00:00',N'执行后延长',@operator,'2026-11-18T02:00:00',@operator,'2026-11-18T02:00:00',1);
                        END
                        ELSE EXEC dbo.usp_record_berth_event @appointment,'UNBERTHING','2026-11-20T03:00:00',@revision,@operator;
                    END;
                END;
                SET @i+=1;
            END;
            """, new { prefix });
        return await connection.QuerySingleAsync<Scenario>("""
            SELECT @prefix Prefix,
                (SELECT berth_id FROM dbo.berth WHERE berth_code=@prefix) Berth,
                (SELECT berth_id FROM dbo.berth WHERE berth_code=@prefix+'E') EmptyBerth,
                (SELECT berth_id FROM dbo.berth WHERE berth_code=@prefix+'F') ForeignBerth,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-%_[\') First,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-2') WithStay,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-3') Draft,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-11') Confirmed,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-12') Executing,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-13') Completed,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-14') ForeignAppointment,
                (SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@prefix+'-16') InvalidConfirmed,
                (SELECT q.queue_entry_id FROM dbo.v_anchorage_queue q WHERE q.appointment_no=@prefix+'-2' AND q.left_anchorage_at IS NULL) CurrentStay,
                (SELECT r.revision_id FROM dbo.berth_plan_revision r JOIN dbo.berth_plan bp ON bp.berth_plan_id=r.berth_plan_id
                    JOIN dbo.arrival_appointment aa ON aa.appointment_id=bp.appointment_id WHERE aa.appointment_no=@prefix+'-12' AND r.version_no=1) ExecutedRevision;
            """, new { prefix });
    }

    private sealed record Scenario(string Prefix, long Berth, long EmptyBerth, long ForeignBerth, long First,
        long WithStay, long Draft, long Confirmed, long Executing, long Completed, long ForeignAppointment,
        long InvalidConfirmed, long CurrentStay, long ExecutedRevision);

    private static string Id(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static string ScheduleUrl(string from = "2026-11-20T00:00:00Z", string to = "2026-11-21T00:00:00Z") =>
        $"/api/berths/schedule?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

    private static async Task<T> Get<T>(HttpClient client, string endpoint)
    {
        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var username = "berth." + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>()
            .HashPassword(new Account(), password));
        await scope.ServiceProvider.GetRequiredService<IAccountStore>()
            .ManageAsync("create", username, "泊位测试", role, hash, CancellationToken.None);
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login")
        { Content = JsonContent.Create(new { username, password }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
