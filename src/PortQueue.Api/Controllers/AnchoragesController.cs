using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Anchorages;

namespace PortQueue.Api.Controllers;

/// <summary>锚地工作区只向真实登录会话开放，停留选择独立于整个锚地队列。</summary>
[ApiController]
[Authorize(Roles = "ADMIN,DISPATCHER,REVIEWER,RECORDER")]
[Route("api/anchorages")]
public sealed class AnchoragesController(IAnchorageQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnchorageOverview>> Get(
        [FromQuery, Range(typeof(long), "1", "9223372036854775807")] long? appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetAsync(appointmentId, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "主港资料不存在。") : Ok(result);
    }

    [HttpGet("{anchorageId}/queue")]
    public async Task<ActionResult<AnchorageQueueSnapshot>> GetQueue(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long anchorageId,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetQueueAsync(anchorageId, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "锚地不存在。") : Ok(result);
    }

    [HttpGet("/api/anchorage-stays/{queueEntryId}")]
    public async Task<ActionResult<AnchorageStayDetail>> GetStay(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long queueEntryId,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetStayAsync(queueEntryId, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "锚地停留不存在。") : Ok(result);
    }
}
