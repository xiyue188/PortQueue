using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
public sealed class AnchorageReorderTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Fact]
    public async Task ReorderRequiresAuthentication()
    {
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsJsonAsync("/api/anchorage-stays/1/reorder", new { newPosition = 1, reason = "调序", expectedSnapshot = new string('A', 64) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ADMIN", true)]
    [InlineData("DISPATCHER", true)]
    [InlineData("RECORDER", false)]
    [InlineData("REVIEWER", false)]
    public async Task HttpAndSqlRecheckRealRole(string role, bool allowed)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, role); using var session = client;
        var anchor = await Seed(); var before = await Queue(client, anchor); var selected = before.Items[4];
        using var response = await Reorder(client, selected.QueueEntryId, 2, before.Snapshot);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (!allowed)
        {
            await using var db = new SqlConnection(fixture.ConnectionString);
            var denied = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_adjust_queue @id,2,N'角色核对',@actor,@snapshot",
                new { id = selected.QueueEntryId, actor, snapshot = before.Snapshot }));
            Assert.Equal(52302, denied.Number);
            Assert.Equal(before.Snapshot, (await Queue(client, anchor)).Snapshot);
        }
    }

    [Fact]
    public async Task FifthToSecondMovesEveryAffectedMemberAndNoOpWritesNothing()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, "DISPATCHER"); using var session = client;
        var anchor = await Seed(); var before = await Queue(client, anchor); var selected = before.Items[4];
        await using var db = new SqlConnection(fixture.ConnectionString);
        var eventCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_event");
        using var response = await Reorder(client, selected.QueueEntryId, 2, before.Snapshot);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AnchorageReorderResult>())!;
        Assert.Equal(selected.QueueEntryId, result.QueueEntryId); Assert.Equal(5, result.OldPosition); Assert.Equal(2, result.NewPosition); Assert.Equal(4, result.AffectedCount);
        var after = await Queue(client, anchor);
        Assert.Equal(new[] { before.Items[0].QueueEntryId, before.Items[4].QueueEntryId, before.Items[1].QueueEntryId, before.Items[2].QueueEntryId, before.Items[3].QueueEntryId, before.Items[5].QueueEntryId, before.Items[6].QueueEntryId }, after.Items.Select(i => i.QueueEntryId));
        Assert.Equal(result.Snapshot, after.Snapshot); Assert.NotEqual(before.Snapshot, after.Snapshot);
        Assert.Equal(before.OccupiedCount, after.OccupiedCount);
        Assert.Equal(eventCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_event"));
        var changes = (await db.QueryAsync<(int Old, int New)>("SELECT old_position,new_position FROM dbo.queue_adjustment_record WHERE adjusted_by=@actor ORDER BY old_position", new { actor })).ToArray();
        Assert.Equal(new[] { (2, 3), (3, 4), (4, 5), (5, 2) }, changes);
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='queue_adjustment_record' AND operator_id=@actor", new { actor }));
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='anchorage_queue_entry' AND operator_id=@actor", new { actor }));
        using var staleSameTarget = await Reorder(client, selected.QueueEntryId, 2, before.Snapshot);
        Assert.Equal(HttpStatusCode.Conflict, staleSameTarget.StatusCode);
        Assert.Contains("QUEUE_SNAPSHOT_CHANGED", await staleSameTarget.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var auditsBeforeNoOp = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log");
        using var noOp = await Reorder(client, selected.QueueEntryId, 2, after.Snapshot);
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.Equal(0, (await noOp.Content.ReadFromJsonAsync<AnchorageReorderResult>())!.AffectedCount);
        Assert.Equal(auditsBeforeNoOp, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log"));
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.queue_adjustment_record WHERE adjusted_by=@actor", new { actor }));
    }

    [Fact]
    public async Task LastToFirstAndFirstToLastKeepCompleteQueueOrder()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "ADMIN"); using var session = client;
        var anchor = await Seed(); var initial = await Queue(client, anchor);
        using var first = await Reorder(client, initial.Items[6].QueueEntryId, 1, initial.Snapshot);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(7, (await first.Content.ReadFromJsonAsync<AnchorageReorderResult>())!.AffectedCount);
        var middle = await Queue(client, anchor); Assert.Equal(initial.Items[6].QueueEntryId, middle.Items[0].QueueEntryId);
        using var last = await Reorder(client, middle.Items[0].QueueEntryId, 7, middle.Snapshot);
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        Assert.Equal(initial.Items.Select(i => i.QueueEntryId), (await Queue(client, anchor)).Items.Select(i => i.QueueEntryId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("invalid")]
    [InlineData("empty-reason")]
    [InlineData("long-reason")]
    [InlineData("out-of-range")]
    [InlineData("zero")]
    [InlineData("actor")]
    [InlineData("csrf")]
    public async Task InvalidRequestsCannotChangeOrder(string kind)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, _) = await Login(factory, "DISPATCHER"); using var session = client;
        var anchor = await Seed(); var before = await Queue(client, anchor);
        var body = new Dictionary<string, object?>
        { ["newPosition"] = kind == "zero" ? 0 : kind == "out-of-range" ? 8 : 2,
          ["reason"] = kind == "empty-reason" ? " \t　\r\n " : kind == "long-reason" ? new string('a',501) : "安排核对",
          ["expectedSnapshot"] = kind switch { "short" => "abc", "long" => before.Snapshot + "A", "invalid" => new string('G',64), _ => before.Snapshot } };
        if (kind == "missing") body.Remove("expectedSnapshot");
        if (kind == "actor") body["operatorId"] = "1";
        using var response = await Send(client, $"/api/anchorage-stays/{before.Items[4].QueueEntryId}/reorder", body, csrf: kind != "csrf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Snapshot, (await Queue(client, anchor)).Snapshot);
    }

    [Fact]
    public async Task TwoClientsWithOneSnapshotCannotOverwriteEachOther()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (one, _) = await Login(factory, "DISPATCHER"); var (two, _) = await Login(factory, "ADMIN"); using var first = one; using var second = two;
        var anchor = await Seed(); var before = await Queue(one, anchor);
        await using var gate = new SqlConnection(fixture.ConnectionString); await gate.OpenAsync();
        using var transaction = gate.BeginTransaction();
        await gate.ExecuteAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; IF @r<0 THROW 53999,'Test gate failed',1;", transaction: transaction);
        var a = Reorder(one, before.Items[4].QueueEntryId, 2, before.Snapshot);
        var b = Reorder(two, before.Items[6].QueueEntryId, 1, before.Snapshot);
        await Task.Delay(150); Assert.False(a.IsCompleted); Assert.False(b.IsCompleted); transaction.Commit();
        var results = await Task.WhenAll(a, b);
        Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = (await results.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<AnchorageReorderResult>())!;
        Assert.Equal(winner.Snapshot, (await Queue(one, anchor)).Snapshot);
        foreach (var result in results) result.Dispose();
    }

    [Fact]
    public async Task LeaveCommittedWhileReorderWaitsInvalidatesWholeQueueSnapshot()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, "DISPATCHER"); using var session = client;
        var anchor = await Seed(); var before = await Queue(client, anchor);
        await using var gate = new SqlConnection(fixture.ConnectionString); await gate.OpenAsync();
        using var transaction = gate.BeginTransaction();
        await gate.ExecuteAsync("EXEC dbo.usp_record_anchorage_event NULL,NULL,'ANCHORAGE_LEAVE','2026-11-20T03:00:00',@actor,@queue_entry_id=@id",
            new { actor, id = before.Items[0].QueueEntryId }, transaction);
        var pending = Reorder(client, before.Items[4].QueueEntryId, 2, before.Snapshot);
        await Task.Delay(150); Assert.False(pending.IsCompleted); transaction.Commit();
        using var response = await pending; Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await Queue(client, anchor); Assert.Equal(6, after.Items.Count);
        Assert.Equal(before.Items.Skip(1).Select(i => i.QueueEntryId), after.Items.Select(i => i.QueueEntryId));
        Assert.Equal(0, await gate.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.queue_adjustment_record r JOIN dbo.anchorage_queue_entry q ON q.queue_entry_id=r.queue_entry_id WHERE q.anchorage_id=@anchor", new { anchor }));
    }

    [Fact]
    public async Task DatabaseValidatesSnapshotReasonAndMinimumPermissions()
    {
        var anchor = await Seed(); using var scope = fixture.Factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAnchorageQueries>(); var before = (await queries.GetQueueAsync(anchor, CancellationToken.None))!;
        await using var db = new SqlConnection(fixture.ConnectionString); await db.OpenAsync();
        var actor = await db.ExecuteScalarAsync<long>("SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'");
        foreach (var snapshot in new string?[] { null, "abc", new('G',64), before.Snapshot+"A" })
        {
            var invalid = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_adjust_queue @id,2,N'数据库校验',@actor,@snapshot", new { id = before.Items[4].QueueEntryId, actor, snapshot }));
            Assert.Equal(52300, invalid.Number);
        }
        var missing = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_adjust_queue @id,2,N'缺失快照',@actor", new { id = before.Items[4].QueueEntryId, actor }));
        Assert.Equal(201, missing.Number);
        foreach (var reason in new[] { " \t\r\n　 ", new string('a',501) })
        {
            var invalid = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_adjust_queue @id,2,@reason,@actor,@snapshot", new { id = before.Items[4].QueueEntryId, actor, snapshot = before.Snapshot, reason }));
            Assert.Equal(52050, invalid.Number);
        }
        var saved = await db.QuerySingleAsync<AnchorageReorderResult>("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_adjust_queue @id,2,N'最小权限调序',@actor,@snapshot; REVERT;",
            new { id = before.Items[4].QueueEntryId, actor, snapshot = before.Snapshot });
        Assert.Equal(4, saved.AffectedCount); Assert.Equal(saved.Snapshot, (await queries.GetQueueAsync(anchor, CancellationToken.None))!.Snapshot);
    }

    [Fact]
    public async Task AuditFailureRollsBackPositionsAndAdjustmentRows()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, actor) = await Login(factory, "ADMIN"); using var session = client;
        var anchor = await Seed(); var before = await Queue(client, anchor); await using var db = new SqlConnection(fixture.ConnectionString);
        var auditCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log");
        await db.ExecuteAsync("CREATE TRIGGER dbo.trg_test_reorder_audit_failure ON dbo.audit_log AFTER INSERT AS THROW 52888,'Test audit failure',1;");
        try
        {
            using var result = await Reorder(client, before.Items[4].QueueEntryId, 2, before.Snapshot);
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal(before.Snapshot, (await Queue(client, anchor)).Snapshot);
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.queue_adjustment_record WHERE adjusted_by=@actor", new { actor }));
            Assert.Equal(auditCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log"));
        }
        finally { await db.ExecuteAsync("DROP TRIGGER dbo.trg_test_reorder_audit_failure;"); }
    }

    private static async Task<AnchorageQueueSnapshot> Queue(HttpClient client, long anchor) =>
        (await client.GetFromJsonAsync<AnchorageQueueSnapshot>($"/api/anchorages/{anchor}/queue"))!;
    private static Task<HttpResponseMessage> Reorder(HttpClient client, string id, int position, string snapshot) =>
        Send(client, $"/api/anchorage-stays/{id}/reorder", new { newPosition = position, reason = "  调度调整  ", expectedSnapshot = snapshot });
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return await client.SendAsync(request);
    }
    private static async Task<(HttpClient Client, long Actor)> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope(); var username = "reorder." + Guid.NewGuid().ToString("N"); var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), password));
        var actor = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync("create", username, "调序测试", role, hash, CancellationToken.None);
        using var response = await Send(client, "/api/session/login", new { username, password }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, actor);
    }
    private async Task<long> Seed()
    {
        var tag = Guid.NewGuid().ToString("N")[..18]; await using var db = new SqlConnection(fixture.ConnectionString);
        await db.ExecuteAsync("""
            SET QUOTED_IDENTIFIER ON;
            DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS'),@actor bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
            INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
            VALUES(@port,@tag,N'调序测试锚地','OUTER',10,'AVAILABLE');
            DECLARE @a bigint=SCOPE_IDENTITY(),@i int=1,@ship bigint,@call bigint,@ap bigint,@at datetime2(0);
            WHILE @i<=7
            BEGIN
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
                SELECT TOP(1) CONCAT(LEFT(@tag,6),@i),CONCAT(LEFT(@tag,8),@i),CONCAT(N'调序船',@i),ship_type_item_id,180,25,10,30000,shipping_company_id,'ACTIVE' FROM dbo.ship ORDER BY ship_id;
                SET @ship=SCOPE_IDENTITY();
                INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
                VALUES(CONCAT(@tag,@i),@ship,'2026-11-20','2026-11-21','PLANNED');
                SET @call=SCOPE_IDENTITY();
                INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,
                    declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at)
                VALUES(CONCAT(@tag,@i),@call,'2026-11-20','2026-11-21',3,8,1,'APPROVED','2026-11-18',@actor,'2026-11-19');
                SET @ap=SCOPE_IDENTITY(); SET @at=DATEADD(minute,@i,CONVERT(datetime2(0),'2026-11-20'));
                EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_ENTER',@at,@actor;
                SET @i+=1;
            END;
            """, new { tag });
        return await db.ExecuteScalarAsync<long>("SELECT anchorage_id FROM dbo.anchorage WHERE anchorage_code=@tag", new { tag });
    }
}
