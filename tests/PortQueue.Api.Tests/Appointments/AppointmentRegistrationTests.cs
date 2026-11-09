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
using PortQueue.Application.Overview;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>3C真实HTTP与独立SQL库验收：幂等、并发、边界、权限及原子性。</summary>
[Trait("Category", "Database")]
public sealed class AppointmentRegistrationTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);
    private async Task<SqlConnection> Connection() { var c = new SqlConnection(fixture.ConnectionString); await c.OpenAsync(); return c; }
    private async Task<RegistrationInput> Input()
    {
        await using var c = await Connection(); var id = await c.ExecuteScalarAsync<long>("SELECT MIN(ship_id) FROM dbo.ship WHERE status_code='ACTIVE'");
        return new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, "2032-01-01T08:00:00+08:00", "2032-01-03T08:00:00+08:00", 100, 2000.25m,
            "2032-01-01T09:00:00+08:00", "2032-01-02T09:00:00+08:00", 4.25m, 8, 3, false, "  独立验收  ");
    }
    private static async Task<(HttpClient Client, long User)> Login(WebApplicationFactory<Program> factory, string role = "DISPATCHER")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope(); var username = "register." + Guid.NewGuid().ToString("N"); var password = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), password));
        var id = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync("create", username, "登记测试", role, hash, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/session/login", new { username, password })).StatusCode); return (client, id);
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, HttpMethod? method = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!.CsrfToken);
        return await client.SendAsync(request);
    }
    private static async Task<RegistrationResult> Created(HttpClient client, RegistrationInput input)
    {
        var response = await Send(client, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, false));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;
    }
    [Fact]
    public async Task CreateEditSubmitAndReviewPreserveFactsAndAudit()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, actor) = await Login(factory, "ADMIN"); using var c = login;
        var input = await Input(); var result = await Created(c, input);
        var detail = (await c.GetFromJsonAsync<AppointmentDetail>($"/api/appointments/{result.AppointmentId}"))!;
        Assert.Equal("DRAFT", detail.AppointmentStatusCode); Assert.Null(detail.SubmittedAt); Assert.Null(detail.ReviewerName);
        Assert.Equal("独立验收", detail.SpecialRequirement); Assert.Equal(new DateTimeOffset(2032, 1, 1, 1, 0, 0, TimeSpan.Zero), detail.RequestedArrivalAt);
        var edited = await Send(c, $"/api/appointments/{result.AppointmentId}/draft", new EditAppointmentRequest(detail.EditVersion, input with { CargoTeu = 215, SpecialRequirement = "修改草稿" }), HttpMethod.Put);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode); var updated = (await edited.Content.ReadFromJsonAsync<RegistrationResult>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/appointments/{result.AppointmentId}/draft", new EditAppointmentRequest(detail.EditVersion, input), HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/appointments/{result.AppointmentId}/submit", new SubmitAppointmentRequest(updated.EditVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/appointments/{result.AppointmentId}/submit", new SubmitAppointmentRequest(updated.EditVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/appointments/{result.AppointmentId}/draft", new EditAppointmentRequest(updated.EditVersion, input), HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/appointments/{result.AppointmentId}/review", new { decision = "APPROVED", reviewComment = "登记闭环核验" })).StatusCode);
        detail = (await c.GetFromJsonAsync<AppointmentDetail>($"/api/appointments/{result.AppointmentId}"))!;
        Assert.Equal("APPROVED", detail.AppointmentStatusCode); Assert.Equal(215, detail.CargoTeu); Assert.NotNull(detail.SubmittedAt); Assert.NotNull(detail.ReviewedAt);
        await using var db = await Connection(); Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=@id AND operator_id=@actor", new { id = result.AppointmentId, actor }));
    }
    [Fact]
    public async Task CreationKeySurvivesEditsAndConcurrentRetries()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var c = login;
        var input = await Input(); var body = new CreateAppointmentRequest(Guid.NewGuid(), input, false);
        var responses = await Task.WhenAll(Send(c, "/api/appointments", body), Send(c, "/api/appointments", body));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Created)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK));
        var one = (await responses[0].Content.ReadFromJsonAsync<RegistrationResult>())!; var two = (await responses[1].Content.ReadFromJsonAsync<RegistrationResult>())!; Assert.Equal(one.AppointmentId, two.AppointmentId);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, "/api/appointments", body with { Data = input with { DeclaredDraftM = 8.00m } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/appointments/{one.AppointmentId}/draft", new EditAppointmentRequest(one.EditVersion, input with { CargoTeu = 999 }), HttpMethod.Put)).StatusCode);
        var retry = await Send(c, "/api/appointments", body); Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.True((await retry.Content.ReadFromJsonAsync<RegistrationResult>())!.Replayed);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, "/api/appointments", body with { Data = input with { CargoTeu = 1 } })).StatusCode);
        await using var db = await Connection(); Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND operation_type='INSERT' AND record_id=@id", new { id = one.AppointmentId }));
        foreach (var response in responses) response.Dispose();
    }
    [Fact]
    public async Task ConcurrentDraftEditorsCannotOverwriteEachOther()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var c = login; var input = await Input(); var created = await Created(c, input);
        var result = await Task.WhenAll(Send(c, $"/api/appointments/{created.AppointmentId}/draft", new EditAppointmentRequest(created.EditVersion, input with { CargoTeu = 10 }), HttpMethod.Put), Send(c, $"/api/appointments/{created.AppointmentId}/draft", new EditAppointmentRequest(created.EditVersion, input with { CargoTeu = 20 }), HttpMethod.Put));
        Assert.Single(result.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(result.Where(r => r.StatusCode == HttpStatusCode.Conflict)); foreach (var r in result) r.Dispose();
    }
    [Theory]
    [InlineData("past")]
    [InlineData("reverse")]
    [InlineData("hours")]
    [InlineData("draft")]
    [InlineData("decimal")]
    [InlineData("offset")]
    [InlineData("negative")]
    [InlineData("priority")]
    [InlineData("length")]
    [InlineData("ship")]
    [InlineData("port")]
    public async Task InvalidRegistrationCreatesNoOrphanFacts(string scenario)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var c = login; var input = await Input();
        input = scenario switch { "past" => input with { RequestedArrivalAt = "2020-01-01T00:00:00Z", RequestedDepartureAt = "2020-01-02T00:00:00Z" }, "reverse" => input with { RequestedDepartureAt = input.RequestedArrivalAt }, "hours" => input with { EstimatedOperationHours = 25 }, "draft" => input with { DeclaredDraftM = 999 }, "decimal" => input with { CargoWeightT = 1.001m }, "offset" => input with { RequestedArrivalAt = "2032-01-01T08:00:00" }, "negative" => input with { CargoTeu = -1 }, "priority" => input with { PriorityLevel = 0 }, "length" => input with { SpecialRequirement = new string('a', 1001) }, "ship" => input with { ShipId = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) }, _ => input with { NextPortId = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        await using var db = await Connection(); var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_call");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, true))).StatusCode);
        Assert.Equal(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_call"));
    }
    [Theory]
    [InlineData("REVIEWER")]
    [InlineData("RECORDER")]
    public async Task UnauthorizedRolesRejectedByHttpAndDatabase(string role)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, actor) = await Login(factory, role); using var c = login;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), await Input(), false))).StatusCode);
        await using var db = await Connection(); var error = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXEC dbo.usp_save_appointment 'CREATE',@actor", new { actor })); Assert.Equal(52103, error.Number);
    }
    [Fact]
    public async Task AuthenticationCsrfAndUnknownIdentityFieldsAreRequired()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); using var anonymous = factory.CreateClient(); var input = await Input();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, false))).StatusCode);
        var (login, _) = await Login(factory); using var c = login;
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, false), csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, "/api/appointments", new { requestKey = Guid.NewGuid(), data = input, submit = false, operatorId = 1 })).StatusCode);
    }
    [Fact]
    public async Task AuditFailureRollsBackCallAndAppointment()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var c = login; var input = await Input(); await using var db = await Connection();
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_call");
        await db.ExecuteAsync("CREATE TRIGGER dbo.trg_test_registration_audit ON dbo.audit_log AFTER INSERT AS THROW 52888,'Injected audit failure',1;");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Send(c, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, true))).StatusCode); Assert.Equal(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.port_call")); }
        finally { await db.ExecuteAsync("DROP TRIGGER dbo.trg_test_registration_audit;"); }
    }
    [Fact]
    public async Task ShipDisabledAfterDraftCannotBeSubmitted()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var c = login; var input = await Input(); var created = await Created(c, input); await using var db = await Connection();
        await db.ExecuteAsync("UPDATE dbo.ship SET status_code='DISABLED' WHERE ship_id=@id", new { id = input.ShipId });
        try { Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, $"/api/appointments/{created.AppointmentId}/submit", new SubmitAppointmentRequest(created.EditVersion))).StatusCode); Assert.NotNull(await c.GetFromJsonAsync<AppointmentDetail>($"/api/appointments/{created.AppointmentId}")); }
        finally { await db.ExecuteAsync("UPDATE dbo.ship SET status_code='ACTIVE' WHERE ship_id=@id", new { id = input.ShipId }); }
    }
    [Fact]
    public async Task ReadOnlyDestinationsAndCandidatesUseRealRows()
    {
        using var client = fixture.Factory.CreateClient(); var options = (await client.GetFromJsonAsync<QueryPage<RegistrationOption>>("/api/ships"))!; Assert.NotEmpty(options.Items);
        foreach (var kind in new[] { "queue", "plans", "events" }) { var page = (await client.GetFromJsonAsync<QueryPage<BusinessRecord>>($"/api/business/{kind}"))!; Assert.Equal(20, page.PageSize); Assert.True(page.TotalCount >= page.Items.Count); }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/business/invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/ships?page=0")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<QueryPage<RegistrationOption>>("/api/ships?keyword=%25_%5B"))!.Items);
    }

    [Fact]
    public async Task LeastPrivilegeCanExecuteRegistrationButCannotWriteTables()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, actor) = await Login(factory); using var client = login;
        var input = await Input(); await using var db = await Connection();
        var payload = System.Text.Json.JsonSerializer.Serialize(input.ToData(), Json);
        var row = await db.QuerySingleAsync<RegistrationResult>("EXECUTE AS USER='portqueue_probe'; EXEC dbo.usp_save_appointment @action='CREATE',@operator_id=@actor,@payload=@payload,@request_key=@key,@submit=1; REVERT;", new { actor, payload, key = Guid.NewGuid() });
        Assert.Equal("PENDING", row.AppointmentStatusCode);
        var failure = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("EXECUTE AS USER='portqueue_probe'; UPDATE dbo.arrival_appointment SET appointment_status_code='DRAFT' WHERE appointment_id=@id;", new { id = row.AppointmentId }));
        Assert.Equal(229, failure.Number); await db.ExecuteAsync("REVERT;");
        // 候选与三个新查询SQL也须在列授权边界内运行，不能只在LocalDB所有者身份验收。
        foreach (var (name, kind) in new[] { ("RegistrationOptions", "ships"), ("BusinessRecords", "queue"), ("BusinessRecords", "plans"), ("BusinessRecords", "events") })
        {
            await using var stream = typeof(PortQueue.Infrastructure.Overview.SqlBusinessQueries).Assembly.GetManifestResourceStream($"PortQueue.Queries.{name}.sql")!;
            using var reader = new StreamReader(stream);
            var result = await db.QuerySingleAsync("EXECUTE AS USER='portqueue_probe'; " + await reader.ReadToEndAsync() + " REVERT;", new { Kind = kind, Keyword = "", Offset = 0 }); Assert.NotNull(result);
        }
    }

    [Theory]
    [InlineData("type")]
    [InlineData("port")]
    public async Task DisabledReferenceCannotEnterNewAppointment(string kind)
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (login, _) = await Login(factory); using var client = login; var input = await Input(); await using var db = await Connection();
        var id = kind == "type" ? await db.ExecuteScalarAsync<long>("SELECT ship_type_item_id FROM dbo.ship WHERE ship_id=@id", new { id = input.ShipId }) : await db.ExecuteScalarAsync<long>("SELECT MIN(port_id) FROM dbo.port");
        if (kind == "port") input = input with { NextPortId = id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        await db.ExecuteAsync(kind == "type" ? "UPDATE dbo.dictionary_item SET is_enabled=0 WHERE dictionary_item_id=@id" : "UPDATE dbo.port SET status_code='DISABLED' WHERE port_id=@id", new { id });
        try { Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/appointments", new CreateAppointmentRequest(Guid.NewGuid(), input, true))).StatusCode); }
        finally { await db.ExecuteAsync(kind == "type" ? "UPDATE dbo.dictionary_item SET is_enabled=1 WHERE dictionary_item_id=@id" : "UPDATE dbo.port SET status_code='ENABLED' WHERE port_id=@id", new { id }); }
    }

    [Fact]
    public async Task SameCreationKeyCannotBeReplayedByAnotherActor()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(_ => { }); var (one, _) = await Login(factory); var (two, _) = await Login(factory); using var c1 = one; using var c2 = two;
        var body = new CreateAppointmentRequest(Guid.NewGuid(), await Input(), true);
        Assert.Equal(HttpStatusCode.Created, (await Send(c1, "/api/appointments", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c2, "/api/appointments", body)).StatusCode);
    }
}
