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
        Assert.Equal(5, overview.RecentEvents.Count);
        Assert.All(overview.RecentEvents, item => Assert.Equal("ANCHORAGE_ENTER", item.EventTypeCode));
        Assert.Equal(overview.RecentEvents.OrderByDescending(item => item.EventTime), overview.RecentEvents);

        await using var stream = typeof(SqlOverviewQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.Overview.sql")!;
        using var reader = new StreamReader(stream);
        using var permission = new SqlCommand("EXECUTE AS USER='portqueue_probe';\n" + await reader.ReadToEndAsync() + "\nREVERT;", connection);
        using var result = await permission.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(9, result.GetInt64(result.GetOrdinal("PendingAppointments")));
    }
}
