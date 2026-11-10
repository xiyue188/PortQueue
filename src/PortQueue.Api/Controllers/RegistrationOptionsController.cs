using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Controllers;

/// <summary>登记表单的真实基础资料候选；分页和关键词均有限制，不提供后续业务模块。</summary>
[ApiController, Route("api")]
public sealed class RegistrationOptionsController(IRegistrationOptions queries) : ControllerBase
{
    [HttpGet("ships")]
    public async Task<IActionResult> Ships([FromQuery, StringLength(100)] string keyword = "", [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken token = default)
        => Ok(await queries.OptionsAsync("ships", keyword ?? "", page, token));
    [HttpGet("ports")]
    public async Task<IActionResult> Ports([FromQuery, StringLength(100)] string keyword = "", [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken token = default)
        => Ok(await queries.OptionsAsync("ports", keyword ?? "", page, token));
}
