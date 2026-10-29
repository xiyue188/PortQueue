using Microsoft.AspNetCore.Mvc;

namespace PortQueue.Api.Controllers;

/// <summary>
/// 提供不依赖业务数据库的进程存活检查，便于启动验证和自动化测试。
/// </summary>
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
