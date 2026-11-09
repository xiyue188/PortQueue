using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Overview;

namespace PortQueue.Api.Controllers;

/// <summary>查询沿用预约只读访问策略，分页和关键词均有限制；写入口单独授权。</summary>
[ApiController, Route("api")]
public sealed class BusinessQueriesController(IBusinessQueries queries) : ControllerBase
{
    [HttpGet("business/{kind}")]
    public async Task<IActionResult> Records([RegularExpression("^(queue|plans|events)$")] string kind,
        [FromQuery, StringLength(100)] string keyword = "", [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken token = default)
        => Ok(await queries.RecordsAsync(kind, keyword ?? "", page, token));
    [HttpGet("ships")]
    public async Task<IActionResult> Ships([FromQuery, StringLength(100)] string keyword = "", [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken token = default)
        => Ok(await queries.OptionsAsync("ships", keyword ?? "", page, token));
    [HttpGet("ports")]
    public async Task<IActionResult> Ports([FromQuery, StringLength(100)] string keyword = "", [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken token = default)
        => Ok(await queries.OptionsAsync("ports", keyword ?? "", page, token));
}
