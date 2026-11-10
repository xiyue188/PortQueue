using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Api.Controllers;
using PortQueue.Application.Accounts;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>真实HTTP、角色、过程事务与审计联合验收；全部只写随机独立库。</summary>
[Trait("Category", "Database")]
public sealed class AppointmentReviewTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private async Task<SqlConnection> Connection()
    {
        var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); return connection;
    }
    private async Task<long> Pending()
    {
        await using var connection = await Connection();
        return await connection.QuerySingleAsync<long>("""
            INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,previous_port_id,cargo_teu,cargo_weight_t,call_status_code)
            SELECT @no,MIN(ship_id),'2030-01-01','2030-01-03',(SELECT port_id FROM dbo.port WHERE port_code='SH-YS'),120,2200,'PLANNED' FROM dbo.ship;
            DECLARE @call bigint=SCOPE_IDENTITY();
            INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,declared_draft_m,priority_level,dangerous_goods_flag,special_requirement,appointment_status_code,submitted_at)
            VALUES(@no,@call,'2030-01-01T01:00:00','2030-01-02',5.5,8.25,2,1,N'危险品单独核验','PENDING',SYSUTCDATETIME());
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """, new { no = "R-" + Guid.NewGuid().ToString("N") });
    }
    private static async Task<(HttpClient Client, long UserId)> Login(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope(); var username = "review." + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), password));
        var id = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync("create", username, "审核测试", role, hash, CancellationToken.None);
        var session = (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login") { Content = JsonContent.Create(new { username, password }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return (client, id);
    }
    private static async Task<HttpResponseMessage> Review(HttpClient client, long id, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/appointments/{id}/review") { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return await client.SendAsync(request);
    }
    private async Task AssertUnchanged(long id)
    {
        await using var connection = await Connection();
        var row = await connection.QuerySingleAsync<(string Status, long? Reviewer, DateTime? Time)>("SELECT appointment_status_code,reviewed_by,reviewed_at FROM dbo.arrival_appointment WHERE appointment_id=@id", new { id });
        Assert.Equal("PENDING", row.Status); Assert.Null(row.Reviewer); Assert.Null(row.Time);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@id)", new { id }));
    }
    [Theory]
    [InlineData("REVIEWER", "APPROVED")]
    [InlineData("ADMIN", "REJECTED")]
    public async Task ReviewCommitsIdentityTimeAndExactlyOneSnapshot(string role, string decision)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (client, userId) = await Login(factory, role); using var clientScope = client;
        var id = await Pending();
        using var response = await Review(client, id, new { decision, reviewComment = "  资料已核验  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await client.GetFromJsonAsync<AppointmentDetail>($"/api/appointments/{id}"))!;
        Assert.Equal(decision, detail.AppointmentStatusCode); Assert.Equal("资料已核验", detail.ReviewComment); Assert.NotNull(detail.ReviewedAt);
        await using var connection = await Connection();
        Assert.Equal(userId, await connection.ExecuteScalarAsync<long>("SELECT reviewed_by FROM dbo.arrival_appointment WHERE appointment_id=@id", new { id }));
        var audit = await connection.QuerySingleAsync<(long Operator, string Before, string After)>("SELECT operator_id,old_data_json,new_data_json FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@id)", new { id });
        Assert.Equal(userId, audit.Operator); Assert.Contains("PENDING", audit.Before); Assert.Contains(decision, audit.After);
        Assert.Equal(HttpStatusCode.Conflict, (await Review(client, id, new { decision, reviewComment = "重复" })).StatusCode);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@id)", new { id }));
    }
    [Fact]
    public async Task FullDeclarationMatchesSqlAndMinimumPermissions()
    {
        var id = await Pending(); using var client = fixture.Factory.CreateClient();
        var detail = (await client.GetFromJsonAsync<AppointmentDetail>($"/api/appointments/{id}"))!;
        Assert.Equal(5.5m, detail.EstimatedOperationHours); Assert.Equal(8.25m, detail.DeclaredDraftM); Assert.Equal(2, detail.PriorityLevel);
        Assert.True(detail.DangerousGoodsFlag); Assert.Equal("危险品单独核验", detail.SpecialRequirement); Assert.Equal(120, detail.CargoTeu); Assert.Equal(2200m, detail.CargoWeightT);
        Assert.NotEmpty(detail.ShipType); Assert.NotEmpty(detail.CompanyName); Assert.NotNull(detail.PreviousPortName); Assert.Null(detail.NextPortName);
        Assert.NotEqual(detail.RequestedArrivalAt, detail.PlannedArrivalAt);
        await using var connection = await Connection();
        using var stream = typeof(PortQueue.Infrastructure.Appointments.SqlAppointmentQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.AppointmentDetail.sql")!;
        using var reader = new StreamReader(stream); var sql = await reader.ReadToEndAsync();
        var permitted = await connection.QuerySingleAsync<AppointmentDetail>("EXECUTE AS USER='portqueue_probe'; " + sql + " REVERT;", new { AppointmentId = id });
        Assert.Equal(detail.SpecialRequirement, permitted.SpecialRequirement);
    }
    [Fact]
    public async Task AnonymousWrongRolesAndSpoofedOperatorCannotWrite()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var id = await Pending();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Review(anonymous, id, new { decision = "APPROVED" }, false)).StatusCode);
        foreach (var role in new[] { "DISPATCHER", "RECORDER" })
        {
            var (client, userId) = await Login(factory, role); using var clientScope = client;
            Assert.Equal(HttpStatusCode.Forbidden, (await Review(client, id, new { decision = "APPROVED" })).StatusCode);
            await using var connection = await Connection();
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("EXEC dbo.usp_review_appointment @id,@userId,'APPROVED'", new { id, userId }));
            Assert.Equal(52092, error.Number);
        }
        var (reviewer, _) = await Login(factory, "REVIEWER"); using var reviewerScope = reviewer;
        Assert.Equal(HttpStatusCode.BadRequest, (await Review(reviewer, id, new { decision = "APPROVED", reviewerId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Review(reviewer, id, new { decision = "APPROVED" }, false)).StatusCode);
        await AssertUnchanged(id);
    }
    [Theory]
    [InlineData("INVALID", "reason")]
    [InlineData("REJECTED", null)]
    [InlineData("REJECTED", " \t\r\n　 ")]
    [InlineData("APPROVED", "LONG")]
    public async Task InvalidReviewLeavesNoBusinessChanges(string decision, string? comment)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (client, _) = await Login(factory, "REVIEWER"); using var clientScope = client;
        var id = await Pending();
        Assert.Equal(HttpStatusCode.BadRequest, (await Review(client, id, new { decision, reviewComment = comment == "LONG" ? new string('a', 1001) : comment })).StatusCode);
        await AssertUnchanged(id);
        Assert.Equal(HttpStatusCode.NotFound, (await Review(client, long.MaxValue, new { decision = "APPROVED" })).StatusCode);
    }
    [Fact]
    public async Task TwoIndependentReviewersProduceExactlyOneWinner()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        var (one, _) = await Login(factory, "REVIEWER"); var (two, _) = await Login(factory, "REVIEWER"); using var a = one; using var b = two;
        var id = await Pending();
        // 先占用真实数据库事务锁，让两个HTTP请求同时等待，释放后验证只有一个提交。
        await using var gate = await Connection();
        using var transaction = gate.BeginTransaction();
        await gate.ExecuteAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; IF @r<0 THROW 53999,'Test lock failed',1;", transaction: transaction);
        var first = Review(one, id, new { decision = "APPROVED" });
        var second = Review(two, id, new { decision = "REJECTED", reviewComment = "冲突审核" });
        await Task.Delay(200);
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        transaction.Commit();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var connection = await Connection();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@id)", new { id }));
        foreach (var result in results) result.Dispose();
    }
    [Fact]
    public async Task AuditFailureRollsBackReview()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (client, _) = await Login(factory, "REVIEWER"); using var clientScope = client;
        var id = await Pending(); await using var connection = await Connection();
        await connection.ExecuteAsync("CREATE TRIGGER dbo.trg_test_review_audit_failure ON dbo.audit_log AFTER INSERT AS THROW 52888,'Injected failure',1;");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Review(client, id, new { decision = "APPROVED" })).StatusCode); await AssertUnchanged(id); }
        finally { await connection.ExecuteAsync("DROP TRIGGER dbo.trg_test_review_audit_failure;"); }
    }
}
