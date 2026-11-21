using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PortQueue.Api.Controllers;
using PortQueue.Api.Security;
using PortQueue.Api.Tests.Appointments;
using PortQueue.Application.Mobile;
namespace PortQueue.Api.Tests.Mobile;
[Trait("Category", "Database")]
public sealed class MobileHistoryTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Fact]
    public async Task HistoryUsesRealConfirmationExcludesDraftsAndPagesWithoutLosingPreviousValue()
    {
        await fixture.ApplyScriptAsync("12_refresh_sample_labels.sql");
        await fixture.ApplyScriptAsync("13_seed_port_scenarios.sql");
        await fixture.ApplyScriptAsync("mobile/07_plan_history.sql");
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mobile:Enabled"] = "true" })));
        await DemoAccounts.SeedAsync(factory.Services);
        using var client = await Login(factory, "pq.recorder");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var basis = await connection.QuerySingleAsync<Basis>("""
            SELECT TOP(1) p.appointment_id AppointmentId,p.berth_plan_id PlanId,r.berth_id BerthId
            FROM dbo.berth_plan p JOIN dbo.berth_plan_revision r ON r.berth_plan_id=p.berth_plan_id
            JOIN dbo.arrival_appointment a ON a.appointment_id=p.appointment_id
            WHERE p.plan_status_code='CONFIRMED' AND a.appointment_status_code='APPROVED'
              AND r.confirmed_at IS NOT NULL AND r.is_current=1 ORDER BY p.berth_plan_id;
            """);
        var op = await connection.ExecuteScalarAsync<long>("SELECT user_id FROM dbo.user_account WHERE username='pq.dispatcher'");
        async Task<long> Create(string reason, int day)
        {
            var row = await connection.QuerySingleAsync<Created>("dbo.usp_create_berth_plan_revision", new
            {
                appointment_id = basis.AppointmentId,
                berth_id = basis.BerthId,
                planned_berthing_at = new DateTime(2031, 1, day, 8, 0, 0),
                planned_unberthing_at = new DateTime(2031, 1, day, 16, 0, 0),
                revision_reason = reason,
                operator_id = op
            }, commandType: System.Data.CommandType.StoredProcedure);
            return row.revision_id;
        }
        var initial = (await client.GetFromJsonAsync<VisitPlanHistory>($"/api/mobile/appointments/{basis.AppointmentId}/changes"))!;
        Assert.NotEmpty(initial.Items);
        var draft = await Create("未确认候选不得公开", 2);
        var published = await Create("结合现场情况调整靠泊时间", 3);
        await connection.ExecuteAsync("dbo.usp_confirm_berth_plan", new { berth_plan_id = basis.PlanId, revision_id = published, operator_id = op }, commandType: System.Data.CommandType.StoredProcedure);
        await Create("后续草稿仍不得公开", 4);
        var first = (await client.GetFromJsonAsync<VisitPlanHistory>($"/api/mobile/appointments/{basis.AppointmentId}/changes?page=1&pageSize=1"))!;
        Assert.Equal(initial.TotalCount + 1, first.TotalCount);
        var current = Assert.Single(first.Items);
        Assert.Equal(published.ToString(System.Globalization.CultureInfo.InvariantCulture), current.RevisionId);
        Assert.Equal(initial.Items[0].VersionNo, current.PreviousVersionNo);
        Assert.Equal(initial.Items[0].PlannedBerthingAt, current.PreviousBerthingAt);
        Assert.Equal("结合现场情况调整靠泊时间", current.Reason);
        Assert.Equal(TimeSpan.Zero, current.PublishedAt.Offset);
        var second = (await client.GetFromJsonAsync<VisitPlanHistory>($"/api/mobile/appointments/{basis.AppointmentId}/changes?page=2&pageSize=1"))!;
        Assert.Equal(initial.Items[0].RevisionId, Assert.Single(second.Items).RevisionId);
        Assert.Null(second.Items[0].PreviousVersionNo);
        Assert.DoesNotContain(first.Items.Concat(second.Items), x => x.RevisionId == draft.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var empty = (await client.GetFromJsonAsync<VisitPlanHistory>("/api/mobile/appointments/9007199254740993/changes"))!;
        Assert.Empty(empty.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/mobile/appointments/9223372036854775807/changes")).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=0", "pageSize=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/mobile/appointments/{basis.AppointmentId}/changes?{query}")).StatusCode);
        await connection.ExecuteAsync("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_mobile_plan_history @id,1,20; REVERT;", new { id = basis.AppointmentId });
        using var limited = await Login(factory, "pq.reviewer");
        Assert.Equal(HttpStatusCode.OK, (await limited.GetAsync($"/api/mobile/appointments/{basis.AppointmentId}/changes")).StatusCode);
        await connection.ExecuteAsync("INSERT dbo.role(role_code,role_name,role_description,status_code) VALUES('LIMITED',N'受限',N'测试权限边界','ENABLED'); UPDATE dbo.user_account SET role_code='LIMITED' WHERE username='pq.reviewer';");
        Assert.Equal(HttpStatusCode.Unauthorized, (await limited.GetAsync($"/api/mobile/appointments/{basis.AppointmentId}/changes")).StatusCode);
    }
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login") { Content = JsonContent.Create(new { username, password = "123456" }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return client;
    }
    private sealed record Basis(long AppointmentId, long PlanId, long BerthId);
    private sealed record Created(long berth_plan_id, long revision_id, int version_no);
}
