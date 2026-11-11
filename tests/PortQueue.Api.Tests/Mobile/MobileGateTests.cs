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
        foreach (var path in new[] { "/api/mobile/session/capabilities", "/api/mobile/appointments/1/overview" }) Assert.Equal(expected, (int)(await client.GetAsync(path)).StatusCode);
    }
}
