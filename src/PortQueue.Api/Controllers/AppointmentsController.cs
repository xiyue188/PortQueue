using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Api.Appointments;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Controllers;

/// <summary>提供预约只读列表；HTTP 参数处理与 SQL 实现分离。</summary>
[ApiController]
[Route("api/appointments")]
public sealed class AppointmentsController(IAppointmentQueries queries) : ControllerBase
{
    [HttpGet("{appointmentId}")]
    [ProducesResponseType<AppointmentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AppointmentDetail>> GetById(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long appointmentId,
        CancellationToken cancellationToken)
    {
        var item = await queries.GetByIdAsync(appointmentId, cancellationToken);
        return item is null ? Problem(statusCode: StatusCodes.Status404NotFound, title: "预约不存在。") : Ok(item);
    }

    [HttpGet]
    [ProducesResponseType<AppointmentPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AppointmentPage>> Get(
        [FromQuery] AppointmentQueryRequest request, CancellationToken cancellationToken)
    {
        return Ok(await queries.GetPageAsync(request.ToQuery(), cancellationToken));
    }
}
