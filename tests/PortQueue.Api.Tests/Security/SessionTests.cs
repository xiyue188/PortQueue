using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Api.Controllers;
using PortQueue.Api.Security;
using PortQueue.Application.Accounts;
using PortQueue.Api.Tests.Appointments;

namespace PortQueue.Api.Tests.Security;

/// <summary>真实 SQL 账号与 HTTP Cookie/CSRF 链路；每个用例独立限速器，数据只写随机测试库。</summary>
[Trait("Category", "Database")]
public sealed class SessionTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private WebApplicationFactory<Program> CreateFactory() => fixture.Factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly)));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<SessionResponse> Session(HttpClient client) => (await client.GetFromJsonAsync<SessionResponse>("/api/session"))!;
    private static async Task<HttpResponseMessage> Login(HttpClient client, string username, string password, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/login") { Content = JsonContent.Create(new { username, password }) };
        request.Headers.Add("X-CSRF-TOKEN", token ?? (await Session(client)).CsrfToken);
        return await client.SendAsync(request);
    }
    private static async Task<(string Username, string Password, long Id)> Account(WebApplicationFactory<Program> factory, string role = "REVIEWER")
    {
        var username = "test." + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N") + "!a9";
        using var scope = factory.Services.CreateScope();
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), password));
        Assert.InRange(hash.Length, 1, 256);
        var id = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync("create", username, "验收账号", role, hash, CancellationToken.None);
        return (username, password, id);
    }
    private async Task Mutate(long id, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LoginUsesRealIdentityAndLogoutClearsSession()
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        var account = await Account(factory);
        Assert.Null((await Session(client)).User);
        using var response = await Login(client, account.Username, account.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), body!.User!.UserId);
        Assert.Equal("审核员", body.User.RoleName);
        Assert.True(body.Permissions.CanReviewAppointment); Assert.False(body.Permissions.CanRegisterAppointment);
        Assert.DoesNotContain(account.Password, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("PortQueue.Session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(account.Username, (await Session(client)).User!.Username);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/session/logout") { Content = JsonContent.Create(new { }) };
        logout.Headers.Add("X-CSRF-TOKEN", body.CsrfToken);
        using var result = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Null((await Session(client)).User);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/test/authorization/review")).StatusCode);
    }

    [Fact]
    public async Task WrongUnknownDisabledAndPlaceholderAccountsAreUniformlyRejected()
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        var account = await Account(factory);
        foreach (var (username, password) in new[] { (account.Username, "wrong"), ("missing.account", "wrong"), ("demo.dispatcher", "wrong") })
        {
            using var response = await Login(client, username, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("INVALID_CREDENTIALS", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await Mutate(account.Id, "UPDATE dbo.user_account SET status_code='LOCKED' WHERE user_id=@id;");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, account.Username, account.Password)).StatusCode);
        Assert.Null((await Session(client)).User);
    }

    [Fact]
    public async Task LoginAndLogoutRequireAntiforgeryToken()
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        var account = await Account(factory);
        using var missing = await client.PostAsJsonAsync("/api/session/login", new { username = account.Username, password = account.Password });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("INVALID_CSRF_TOKEN", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client, account.Username, account.Password, "invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, account.Username, account.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/session/logout", new { })).StatusCode);
        Assert.NotNull((await Session(client)).User);
    }

    [Theory]
    [InlineData("ADMIN", true, true)]
    [InlineData("DISPATCHER", true, false)]
    [InlineData("REVIEWER", false, true)]
    [InlineData("RECORDER", false, false)]
    public async Task PoliciesEnforceRoles(string role, bool register, bool review)
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/test/authorization/review")).StatusCode);
        var account = await Account(factory, role);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, account.Username, account.Password)).StatusCode);
        Assert.Equal(register ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/test/authorization/register")).StatusCode);
        Assert.Equal(review ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/test/authorization/review")).StatusCode);
        var identity = await Session(client);
        Assert.Equal(register, identity.Permissions.CanRegisterAppointment); Assert.Equal(review, identity.Permissions.CanReviewAppointment);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("role-disable")]
    [InlineData("password")]
    [InlineData("role-change")]
    public async Task ChangesTakeEffectOnNextRequest(string change)
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        var account = await Account(factory);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, account.Username, account.Password)).StatusCode);
        try
        {
            await Mutate(account.Id, change switch
            {
                "disable" => "UPDATE dbo.user_account SET status_code='DISABLED' WHERE user_id=@id;",
                "role-disable" => "UPDATE dbo.role SET status_code='DISABLED' WHERE role_code='REVIEWER';",
                "password" => "UPDATE dbo.user_account SET password_hash=CRYPT_GEN_RANDOM(32) WHERE user_id=@id;",
                _ => "UPDATE dbo.user_account SET role_code='RECORDER' WHERE user_id=@id;"
            });
            Assert.Equal(change == "role-change" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                (await client.GetAsync("/test/authorization/review")).StatusCode);
            var session = await Session(client);
            if (change == "role-change") { Assert.Equal("RECORDER", session.User!.RoleCode); Assert.False(session.Permissions.CanReviewAppointment); }
            else Assert.Null(session.User);
        }
        finally { if (change == "role-disable") await Mutate(account.Id, "UPDATE dbo.role SET status_code='ENABLED' WHERE role_code='REVIEWER';"); }
    }

    [Fact]
    public async Task SixthLoginAttemptIsLimited()
    {
        await using var factory = CreateFactory(); using var client = Client(factory);
        var token = (await Session(client)).CsrfToken;
        for (var attempt = 0; attempt < 5; attempt++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "missing", "wrong", token)).StatusCode);
        using var response = await Login(client, "missing", "wrong", token);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task MaintenanceIsAuditedWithoutSecretsAndAppRoleCannotWrite()
    {
        await using var factory = CreateFactory(); var account = await Account(factory);
        using var scope = factory.Services.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        var replacement = Guid.NewGuid().ToString("N");
        var hash = Encoding.UTF8.GetBytes(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(new Account(), replacement));
        await store.ManageAsync("reset-password", account.Username, null, null, hash, CancellationToken.None);
        await store.ManageAsync("disable", account.Username, null, null, null, CancellationToken.None);
        Assert.False((await store.FindAsync(account.Username, null, CancellationToken.None))!.IsEnabled);
        await store.ManageAsync("enable", account.Username, null, null, null, CancellationToken.None);
        Assert.True((await store.FindAsync(account.Username, null, CancellationToken.None))!.IsEnabled);
        await Assert.ThrowsAsync<SqlException>(() => store.ManageAsync("create", account.Username, "重复", "REVIEWER", hash, CancellationToken.None));
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        using var audit = new SqlCommand("SELECT new_data_json FROM dbo.audit_log WHERE table_name='user_account' AND record_id=CONVERT(varchar(100),@id)", connection);
        audit.Parameters.AddWithValue("@id", account.Id);
        using (var reader = await audit.ExecuteReaderAsync())
        {
            var count = 0;
            while (await reader.ReadAsync()) { count++; var json = reader.GetString(0); Assert.DoesNotContain("password_hash", json, StringComparison.Ordinal); Assert.DoesNotContain(replacement, json, StringComparison.Ordinal); Assert.DoesNotContain(Encoding.UTF8.GetString(hash), json, StringComparison.Ordinal); }
            Assert.Equal(4, count);
        }
        using var allowed = new SqlCommand("EXECUTE AS USER='portqueue_probe'; SELECT u.password_hash,r.status_code FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code WHERE u.user_id=@id; REVERT;", connection);
        allowed.Parameters.AddWithValue("@id", account.Id); Assert.NotNull(await allowed.ExecuteScalarAsync());
        using var denied = new SqlCommand("EXECUTE AS USER='portqueue_probe'; UPDATE dbo.user_account SET status_code='DISABLED' WHERE user_id=@id;", connection);
        denied.Parameters.AddWithValue("@id", account.Id);
        await Assert.ThrowsAsync<SqlException>(async () => await denied.ExecuteNonQueryAsync());
    }
}

// 仅测试程序集注册；验证真实策略管道，产品不会开放这些端点。
[ApiController, Route("test/authorization")]
public sealed class AuthorizationProbeController : ControllerBase
{
    [HttpGet("review"), Authorize(Policy = SessionSecurity.ReviewPolicy)] public IActionResult Review() => Ok();
    [HttpGet("register"), Authorize(Policy = SessionSecurity.RegisterPolicy)] public IActionResult Register() => Ok();
}
