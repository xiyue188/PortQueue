using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Overview;

namespace PortQueue.Api.Controllers;

/// <summary>港口总览只读入口，与预约检索条件独立。</summary>
[ApiController]
[Route("api/overview")]
public sealed class OverviewController(IOverviewQueries queries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PortOverview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PortOverview>> Get(CancellationToken cancellationToken,
        [FromQuery, Range(1, 1_000_000, ErrorMessage = "动态页码须为 1 至 1000000 的整数。")] int eventsPage = 1)
        => Ok(await queries.GetAsync(eventsPage, cancellationToken));
}
