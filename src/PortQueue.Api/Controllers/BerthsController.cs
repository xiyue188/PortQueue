using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Berths;

namespace PortQueue.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN,DISPATCHER,REVIEWER,RECORDER")]
[Route("api/berths")]
public sealed partial class BerthsController(IBerthQueries queries) : ControllerBase
{
    [HttpGet("schedule")]
    public async Task<ActionResult<BerthSchedule>> GetSchedule([FromQuery] string? from, [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        // 无 offset 的浏览器输入不能悄悄按服务器本地时区解释。
        if (!TryReadTimestamp(from, out var start) || !TryReadTimestamp(to, out var end)
            || end <= start || end - start > TimeSpan.FromHours(48))
            return Problem(statusCode: 400, title: "请选择带时区的起止时间，范围须大于零且不超过48小时。");
        var result = await queries.GetScheduleAsync(start, end, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "主港资料不存在。") : Ok(result);
    }

    [HttpGet("visits")]
    public Task<BerthVisitPage> GetVisits(
        [FromQuery, RegularExpression("^(waiting|all)$")] string scope = "waiting",
        [FromQuery, StringLength(100)] string? keyword = null,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 8,
        CancellationToken cancellationToken = default) => queries.GetVisitsAsync(scope, keyword ?? "", page, pageSize, cancellationToken);

    [HttpGet("visits/{appointmentId}")]
    public async Task<ActionResult<BerthVisitDetail>> GetVisit(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetVisitAsync(appointmentId, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "泊位到访不存在。") : Ok(result);
    }

    private static bool TryReadTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null && TimestampPattern().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();
}
