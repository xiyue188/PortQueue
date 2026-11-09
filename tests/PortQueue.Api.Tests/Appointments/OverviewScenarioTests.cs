using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using PortQueue.Application.Overview;
using PortQueue.Infrastructure.Overview;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>独立库验证旧样例升级、48条场景重复装载、总览事实与最小读取权限。</summary>
[Trait("Category", "Database")]
public sealed class OverviewScenarioTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Fact]
    public async Task ScenarioUpgradeIsRepeatableAndOverviewMatchesFacts()
    {
        using var client = fixture.Factory.CreateClient();
        var before = (await client.GetFromJsonAsync<PortOverview>("/api/overview"))!;
        Assert.Equal(1, before.PendingAppointments);
        Assert.Equal(2, before.AnchorageVessels);
        Assert.Equal(0, before.ConfirmedPlans);
        await fixture.ApplyScriptAsync("12_refresh_sample_labels.sql");
        await fixture.ApplyScriptAsync("13_seed_port_scenarios.sql");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        async Task<long> Scalar(string sql)
        {
            using var cmd = new SqlCommand(sql, connection);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        var audit = await Scalar("SELECT COUNT_BIG(*) FROM dbo.audit_log");
        // 再执行旧脚本也不能复制已经改名的五条记录。
        await fixture.ApplyScriptAsync("10_seed_demo.sql");
        await fixture.ApplyScriptAsync("12_refresh_sample_labels.sql");
        await fixture.ApplyScriptAsync("13_seed_port_scenarios.sql");
        Assert.Equal(audit, await Scalar("SELECT COUNT_BIG(*) FROM dbo.audit_log"));
        Assert.Equal(56, await Scalar("SELECT COUNT_BIG(*) FROM dbo.arrival_appointment")); // 53 场景 + 3 边界记录。
        Assert.Equal(0, await Scalar("SELECT COUNT_BIG(*) FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ-DEMO-%'"));
        Assert.Equal(48, await Scalar("SELECT COUNT_BIG(*) FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ202611-10%'"));
        Assert.Equal(48, await Scalar("SELECT COUNT_BIG(DISTINCT requested_arrival_at) FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ202611-10%'"));
        Assert.Equal(0, await Scalar("SELECT COUNT_BIG(*) FROM dbo.arrival_appointment WHERE appointment_status_code IN ('APPROVED','REJECTED','COMPLETED') AND (reviewed_by IS NULL OR reviewed_at IS NULL)"));
        Assert.Equal(10, await Scalar("SELECT COUNT_BIG(*) FROM dbo.arrival_appointment WHERE appointment_status_code='COMPLETED'"));
        var response = await client.GetAsync("/api/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = (await response.Content.ReadFromJsonAsync<PortOverview>())!;
        Assert.Equal(9, overview.PendingAppointments);
        Assert.Equal(10, overview.AnchorageVessels);
        Assert.Equal(4, overview.ConfirmedPlans);
        Assert.Equal(12, overview.RecentEvents.Count);
        Assert.Equal(6, overview.AppointmentStates.Count);
        Assert.Equal(56, overview.AppointmentStates.Sum(state => state.Count));
        Assert.Equal(10, overview.AppointmentStates.Single(state => state.Code == "APPROVED").Count);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_anchorage_queue WHERE queue_status_code='WAITING'"), overview.WaitingQueue);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_anchorage_queue WHERE queue_status_code='ASSIGNED'"), overview.AssignedQueue);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_current_berth_plan WHERE plan_status_code='DRAFT'"), overview.DraftPlans);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_current_berth_plan WHERE plan_status_code='EXECUTING'"), overview.ExecutingPlans);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_port_event WHERE event_type_code='PORT_ENTER'"), overview.PortEntries);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_port_event WHERE event_type_code='BERTHING'"), overview.Berthings);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.v_port_event WHERE event_type_code='UNBERTHING'"), overview.Unberthings);
        Assert.Equal(await Scalar("SELECT COUNT_BIG(*) FROM dbo.port_event"), overview.TotalEvents);
        Assert.Equal(overview.RecentEvents.OrderByDescending(item => item.EventTime), overview.RecentEvents);
        // 遍历所有页，核对数量、主键唯一和跨页时间顺序，避免只是增大 TOP 上限。
        var events = new List<RecentPortEvent>();
        for (var page = 1; page <= Math.Ceiling(overview.TotalEvents / 12d); page++)
        {
            var batch = (await client.GetFromJsonAsync<PortOverview>($"/api/overview?eventsPage={page}"))!;
            Assert.Equal(page, batch.EventsPage);
            Assert.Equal(12, batch.EventsPageSize);
            Assert.Equal(overview.TotalEvents, batch.TotalEvents);
            events.AddRange(batch.RecentEvents);
        }
        Assert.Equal(overview.TotalEvents, events.Count);
        Assert.Equal(events.Count, events.Select(item => item.EventId).Distinct().Count());
        Assert.Equal(events.OrderByDescending(item => item.EventTime).ThenByDescending(item => long.Parse(item.EventId, System.Globalization.CultureInfo.InvariantCulture)), events);
        var empty = (await client.GetFromJsonAsync<PortOverview>("/api/overview?eventsPage=1000000"))!;
        Assert.Empty(empty.RecentEvents);
        Assert.Equal(overview.TotalEvents, empty.TotalEvents);

        await using var stream = typeof(SqlOverviewQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.Overview.sql")!;
        using var reader = new StreamReader(stream);
        using var permission = new SqlCommand("EXECUTE AS USER='portqueue_probe';\n" + await reader.ReadToEndAsync() + "\nREVERT;", connection);
        permission.Parameters.AddWithValue("@HomePortCode", "CNTAO");
        permission.Parameters.AddWithValue("@EventOffset", 0);
        permission.Parameters.AddWithValue("@EventPageSize", 12);
        using var result = await permission.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(9, result.GetInt64(result.GetOrdinal("PendingAppointments")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("abc")]
    public async Task InvalidEventPageReturnsValidationError(string page)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/overview?eventsPage=" + page);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
