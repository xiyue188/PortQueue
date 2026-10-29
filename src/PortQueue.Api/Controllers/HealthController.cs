using Microsoft.AspNetCore.Mvc;

namespace PortQueue.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "PortQueue.Api",
        status = "ok",
        utc = DateTimeOffset.UtcNow
    });
}
