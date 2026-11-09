using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>HTTP 参数与故障契约；故障用真实不可达 SQL 端点触发，不停止用户的 LocalDB。</summary>
public sealed class AppointmentHttpContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppointmentHttpContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PortQueue"] = "Server=tcp:127.0.0.1,1;Database=PortQueue;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1;ConnectRetryCount=0;Pooling=False"
            })));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=1000001")]
    [InlineData("page=999999999999999999")]
    [InlineData("page=abc")]
    [InlineData("page=1.5")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=-1")]
    [InlineData("status=INVALID")]
    public async Task InvalidParametersReturn400BeforeDatabaseAccess(string query)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/appointments?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_QUERY", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task InvalidDetailKeyIsRejectedBeforeDatabaseAccess()
    {
        using var client = _factory.CreateClient();
        foreach (var id in new[] { "0", "-1", "9223372036854775808", "abc" })
        {
            using var response = await client.GetAsync("/api/appointments/" + id);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task OverlongKeywordIsRejected()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/appointments?keyword=" + new string('a', 101));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/appointments")]
    [InlineData("/api/overview")]
    [InlineData("/api/appointments/1")]
    public async Task UnreachableSqlReturns503WithoutSensitiveDetails(string endpoint)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal("DATABASE_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
        foreach (var secret in new[] { "127.0.0.1", "SqlException", "SELECT", "ConnectionString", "StackTrace" })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        using var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
