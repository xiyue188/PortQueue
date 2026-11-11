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
public sealed class MobileOverviewTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private WebApplicationFactory<Program> Factory() => fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mobile:Enabled"] = "true" })));
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory)
    {
        await DemoAccounts.SeedAsync(factory.Services);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login") { Content = JsonContent.Create(new { username = "pq.admin", password = "123456" }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return client;
    }
    [Fact]
    public async Task RealCookieLoginReturnsStringIdentityUtcAndNoUnavailableWriteCapabilities()
    {
        await using var factory = Factory();
        using var client = await Login(factory);
        using var cap = JsonDocument.Parse(await client.GetStringAsync("/api/mobile/session/capabilities"));
        Assert.False(cap.RootElement.GetProperty("canSubmitMobileReport").GetBoolean());
        Assert.False(cap.RootElement.GetProperty("canProcessMobileReport").GetBoolean());
        var before = DateTimeOffset.UtcNow;
        var value = (await client.GetFromJsonAsync<VisitOverview>("/api/mobile/appointments/9007199254740993/overview"))!;
        Assert.Equal("9007199254740993", value.AppointmentId);
        Assert.Equal("S2-CALL-1", value.CallNo);
        Assert.Null(value.Queue);
        Assert.Null(value.PublishedPlan);
        Assert.Null(value.LatestAcceptedEta);
        Assert.False(value.CanSubmitReport);
        Assert.Equal(TimeSpan.Zero, value.ServerTime.Offset);
        Assert.Equal(TimeSpan.Zero, value.PlannedArrivalAt.Offset);
        Assert.InRange(value.ServerTime, before, DateTimeOffset.UtcNow);
        foreach (var id in new[] { "0", "-1", "abc", "9223372036854775808" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/mobile/appointments/{id}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/mobile/appointments/9223372036854775807/overview")).StatusCode);
    }
    [Fact]
    public async Task ScenarioReadsOnlySelectedVisitPublishedCurrentRevisionAndEventFactsWithLeastPrivilege()
    {
        await fixture.ApplyScriptAsync("12_refresh_sample_labels.sql");
        await fixture.ApplyScriptAsync("13_seed_port_scenarios.sql");
        await using var factory = Factory();
        using var client = await Login(factory);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using (var executing = new SqlCommand("UPDATE dbo.berth_plan SET plan_status_code='EXECUTING' WHERE berth_plan_id=(SELECT MIN(berth_plan_id) FROM dbo.berth_plan WHERE plan_status_code='CONFIRMED');", connection)) await executing.ExecuteNonQueryAsync();
        var cases = new List<(long Id, string Call, string? Anchorage, string? QueueStatus, DateTime? Entered, DateTime? Left, string? Berth, int? Version, string? PlanStatus)>();
        using (var command = new SqlCommand("""
   SELECT aa.appointment_id,pc.call_no,q.anchorage_name,q.queue_status_code,q.entered_anchorage_at,q.left_anchorage_at,p.berth_name,p.version_no,p.plan_status_code
   FROM dbo.arrival_appointment aa JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
   OUTER APPLY(SELECT TOP(1) v.* FROM dbo.v_anchorage_queue v WHERE v.appointment_id=aa.appointment_id ORDER BY CASE WHEN v.queue_status_code IN('WAITING','ASSIGNED') THEN 0 ELSE 1 END,v.created_at DESC,v.queue_entry_id DESC) q
   OUTER APPLY(SELECT TOP(1) v.* FROM dbo.v_current_berth_plan v WHERE v.appointment_id=aa.appointment_id AND v.plan_status_code IN('CONFIRMED','EXECUTING') AND v.confirmed_at IS NOT NULL AND v.confirmed_by IS NOT NULL ORDER BY v.berth_plan_id DESC) p;
   """, connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) cases.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        Assert.Contains(cases, c => c.PlanStatus == "CONFIRMED");
        Assert.Contains(cases, c => c.PlanStatus == "EXECUTING");
        Assert.Contains(cases, c => c.Left is not null && c.Entered is not null);
        Assert.Contains(cases, c => c.QueueStatus == "WAITING");
        foreach (var row in cases)
        {
            var value = (await client.GetFromJsonAsync<VisitOverview>($"/api/mobile/appointments/{row.Id}/overview"))!;
            Assert.Equal(row.Call, value.CallNo); // every appointment stays linked to its unique port call
            Assert.Equal(row.Anchorage, value.Queue?.AnchorageName);
            Assert.Equal(row.QueueStatus, value.Queue?.StatusCode);
            Assert.Equal(row.Entered, value.Queue?.EnteredAt?.UtcDateTime);
            Assert.Equal(row.Left, value.Queue?.LeftAt?.UtcDateTime);
            if (row.Left is not null) Assert.Equal(row.Left - row.Entered, value.Queue!.LeftAt - value.Queue.EnteredAt); // ended waiting uses facts, not query time
            Assert.Equal(row.Berth, value.PublishedPlan?.BerthName);
            Assert.Equal(row.Version, value.PublishedPlan?.VersionNo);
            Assert.Equal(row.PlanStatus, value.PublishedPlan?.StatusCode);
        }
        // 同一船的两个独立航次：第一条有已取消队列，第二条不能继承它。
        using (var cancelled = new SqlCommand("""
            INSERT dbo.anchorage_queue_entry(appointment_id,anchorage_id,queue_position,priority_level,queue_status_code)
            VALUES(9007199254740995,(SELECT MIN(anchorage_id) FROM dbo.anchorage),NULL,1,'CANCELLED');
            """, connection))
        {
            await cancelled.ExecuteNonQueryAsync();
        }
        var firstVisit = (await client.GetFromJsonAsync<VisitOverview>("/api/mobile/appointments/9007199254740995/overview"))!;
        var secondVisit = (await client.GetFromJsonAsync<VisitOverview>("/api/mobile/appointments/9007199254740994/overview"))!;
        Assert.Equal("CANCELLED", firstVisit.Queue!.StatusCode);
        Assert.Null(firstVisit.Queue.EnteredAt);
        Assert.Null(firstVisit.Queue.LeftAt); // 撤销队列不能伪造实际离锚事实。
        Assert.Equal("S2-CALL-2", secondVisit.CallNo);
        Assert.Null(secondVisit.Queue);
        // 新历史比有效队列更新，仍必须返回有效队列，不能简单按时间取最后一条。
        var active = cases.First(row => row.QueueStatus == "WAITING");
        using (var history = new SqlCommand("""
            INSERT dbo.anchorage_queue_entry(appointment_id,anchorage_id,queue_position,priority_level,queue_status_code,created_at)
            SELECT TOP(1) appointment_id,anchorage_id,NULL,priority_level,'CANCELLED','2099-01-01'
            FROM dbo.anchorage_queue_entry WHERE appointment_id=@id AND queue_status_code='WAITING';
            """, connection))
        {
            history.Parameters.AddWithValue("@id", active.Id);
            await history.ExecuteNonQueryAsync();
        }
        Assert.Equal("WAITING", (await client.GetFromJsonAsync<VisitOverview>($"/api/mobile/appointments/{active.Id}/overview"))!.Queue!.StatusCode);

        // 未确认候选不是已发布版本；即使人为成为当前，也不允许回退泄露旧确认版本。
        var confirmed = cases.First(row => row.PlanStatus == "CONFIRMED");
        using (var candidate = new SqlCommand("""
            INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,revision_reason,revised_by,is_current)
            SELECT berth_plan_id,version_no+1,berth_id,planned_berthing_at,DATEADD(hour,1,planned_unberthing_at),N'M1查询边界夹具',revised_by,0
            FROM dbo.berth_plan_revision WHERE is_current=1 AND berth_plan_id=(SELECT berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@id);
            """, connection))
        {
            candidate.Parameters.AddWithValue("@id", confirmed.Id);
            await candidate.ExecuteNonQueryAsync();
        }
        Assert.Equal(confirmed.Version, (await client.GetFromJsonAsync<VisitOverview>($"/api/mobile/appointments/{confirmed.Id}/overview"))!.PublishedPlan!.VersionNo);
        using (var switchCurrent = new SqlCommand("""
            DECLARE @plan bigint=(SELECT berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@id);
            UPDATE dbo.berth_plan_revision SET is_current=0 WHERE berth_plan_id=@plan AND is_current=1;
            UPDATE dbo.berth_plan_revision SET is_current=1 WHERE berth_plan_id=@plan AND confirmed_at IS NULL;
            """, connection))
        {
            switchCurrent.Parameters.AddWithValue("@id", confirmed.Id);
            await switchCurrent.ExecuteNonQueryAsync();
        }
        Assert.Null((await client.GetFromJsonAsync<VisitOverview>($"/api/mobile/appointments/{confirmed.Id}/overview"))!.PublishedPlan);
        using (var makeDraft = new SqlCommand("UPDATE dbo.berth_plan SET plan_status_code='DRAFT' WHERE berth_plan_id=(SELECT MAX(berth_plan_id) FROM dbo.berth_plan WHERE plan_status_code='CONFIRMED');", connection)) await makeDraft.ExecuteNonQueryAsync();
        using (var draft = new SqlCommand("SELECT TOP(1) appointment_id FROM dbo.v_current_berth_plan WHERE plan_status_code='DRAFT'", connection))
        {
            var id = (long)(await draft.ExecuteScalarAsync())!;
            Assert.Null((await client.GetFromJsonAsync<VisitOverview>($"/api/mobile/appointments/{id}/overview"))!.PublishedPlan);
        }
        await using var stream = typeof(PortQueue.Infrastructure.Mobile.SqlMobileVisitQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.MobileVisitOverview.sql")!;
        using var text = new StreamReader(stream);
        using var permission = new SqlCommand("EXECUTE AS USER='portqueue_probe';\n" + await text.ReadToEndAsync() + "\nREVERT;", connection);
        permission.Parameters.AddWithValue("@AppointmentId", cases[0].Id);
        using var result = await permission.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
    }
}
