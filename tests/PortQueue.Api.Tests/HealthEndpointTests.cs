using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PortQueue.Api.Tests;

/// <summary>
/// 验证 API 最小启动闭环，避免把数据库连接问题误判为 Web 进程未启动。
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        using var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RootEndpointReturnsStartupInformation()
    {
        using var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PortQueue.Api", body, StringComparison.Ordinal);
        Assert.Contains("/api/health", body, StringComparison.Ordinal);
    }
}
