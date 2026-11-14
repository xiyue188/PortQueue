using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
namespace PortQueue.Api.Tests.Mobile;
public sealed class MobileGateTests
{
    [Theory]
    [InlineData(null, 404)]
    [InlineData("false", 404)]
    [InlineData("true", 401)]
    public async Task GateRequiresExplicitEnableAndRealAuthentication(string? enabled, int expected)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mobile:Enabled"] = enabled })));
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/mobile/session/capabilities", "/api/mobile/appointments/1/overview", "/api/mobile/reports", "/api/mobile/reports/1", "/api/mobile/photos/1" })
            Assert.Equal(expected, (int)(await client.GetAsync(path)).StatusCode);
        Assert.Equal(expected, (int)(await client.PostAsync("/api/mobile/reports/photo", new MultipartFormDataContent())).StatusCode);
        Assert.Equal(expected, (int)(await client.PostAsync("/api/mobile/reports/eta", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }
}
