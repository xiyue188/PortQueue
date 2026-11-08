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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PortOverview>> Get(CancellationToken cancellationToken)
        => Ok(await queries.GetAsync(cancellationToken));
}
