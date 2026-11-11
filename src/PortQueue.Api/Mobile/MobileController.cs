using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Mobile;

namespace PortQueue.Api.Mobile;

/// <summary>移动端 M1 只读接口，复用现有真实 Cookie 会话。</summary>
[ApiController, Authorize, Route("api/mobile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MobileController(IMobileVisitQueries queries) : ControllerBase
{
    // M1 仅查询；M2 写接口实际可用后才允许对客户端声明报送/处理能力。
    [HttpGet("session/capabilities")]
    public IActionResult Capabilities() => Ok(new
    {
        canSubmitMobileReport = User.IsInRole("ADMIN") || User.IsInRole("DISPATCHER") || User.IsInRole("RECORDER"),
        canProcessMobileReport = User.IsInRole("ADMIN") || User.IsInRole("DISPATCHER")
    });

    [HttpGet("appointments/{appointmentId}/overview")]
    [ProducesResponseType<VisitOverview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitOverview>> Overview(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long appointmentId,
        CancellationToken cancellationToken)
    {
        var value = await queries.GetAsync(appointmentId, cancellationToken);
        if (value is not null)
            value = value with
            {
                CanSubmitReport = (User.IsInRole("ADMIN") || User.IsInRole("DISPATCHER") || User.IsInRole("RECORDER")) && value.AppointmentStatusCode == "APPROVED" && value.CallStatusCode is "PLANNED" or "IN_PROGRESS"
            };
        return value is null ? Problem(statusCode: 404, title: "预约不存在。") : Ok(value);
    }
}
