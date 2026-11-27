using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Anchorages;

namespace PortQueue.Api.Controllers;

[ApiController, Authorize]
public sealed class AnchorageOperationsController(IAnchorageOperations operations, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("api/anchorages/{anchorageId}/entries"), Authorize(Roles = "ADMIN,DISPATCHER,RECORDER")]
    public Task<IActionResult> Enter([Range(typeof(long), "1", "9223372036854775807")] long anchorageId,
        AnchorageEntryRequest request, CancellationToken cancellationToken) => Execute(async actor =>
            await operations.RecordAsync(long.Parse(request.AppointmentId, CultureInfo.InvariantCulture), anchorageId, null,
                "ANCHORAGE_ENTER", AnchorageTime.Parse(request.EventTime), request.Remark?.Trim(), actor, cancellationToken));

    [HttpPost("api/anchorage-stays/{queueEntryId}/leave"), Authorize(Roles = "ADMIN,DISPATCHER,RECORDER")]
    public Task<IActionResult> Leave([Range(typeof(long), "1", "9223372036854775807")] long queueEntryId,
        AnchorageLeaveRequest request, CancellationToken cancellationToken) => Execute(async actor =>
            await operations.RecordAsync(null, null, queueEntryId, "ANCHORAGE_LEAVE", AnchorageTime.Parse(request.EventTime),
                request.Remark?.Trim(), actor, cancellationToken));

    [HttpPut("api/anchorage-stays/{queueEntryId}/estimate"), Authorize(Roles = "ADMIN,DISPATCHER")]
    public Task<IActionResult> Estimate([Range(typeof(long), "1", "9223372036854775807")] long queueEntryId,
        AnchorageEstimateRequest request, CancellationToken cancellationToken) => Execute(async actor =>
            await operations.SetEstimateAsync(queueEntryId, request.ManualEstimatedBerthingAt is null ? null : AnchorageTime.Parse(request.ManualEstimatedBerthingAt),
                long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture), request.Reason.Trim(), actor, cancellationToken));

    private async Task<IActionResult> Execute(Func<long, Task<object>> action)
    {
        try { await antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException) { return Error(400, "会话已变化，请刷新会话后重试。", "INVALID_CSRF_TOKEN"); }
        try { return Ok(await action(long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture))); }
        catch (AnchorageOperationException ex) { return Error(ex.Status, ex.Message, ex.Code); }
    }
    private static ObjectResult Error(int status, string title, string code) => new(new ProblemDetails
    { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnchorageEntryRequest(
    [Required, RegularExpression(@"^[1-9]\d{0,18}$"), Range(typeof(long), "1", "9223372036854775807")] string AppointmentId,
    [Required] string EventTime, [StringLength(500)] string? Remark) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => AnchorageTime.Validate(EventTime, nameof(EventTime));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnchorageLeaveRequest([Required] string EventTime, [StringLength(500)] string? Remark) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => AnchorageTime.Validate(EventTime, nameof(EventTime));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnchorageEstimateRequest(string? ManualEstimatedBerthingAt,
    [Required, RegularExpression(@"^(0|[1-9]\d{0,18})$"), Range(typeof(long), "0", "9223372036854775807")] string ExpectedVersion,
    [Required, StringLength(500)] string Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ManualEstimatedBerthingAt is not null)
            foreach (var result in AnchorageTime.Validate(ManualEstimatedBerthingAt, nameof(ManualEstimatedBerthingAt))) yield return result;
        if (string.IsNullOrWhiteSpace(Reason)) yield return new ValidationResult("请填写修改原因。", [nameof(Reason)]);
    }
}

internal static class AnchorageTime
{
    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).ToUniversalTime();
    public static IEnumerable<ValidationResult> Validate(string? value, string field)
    {
        if (value is null || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.0{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.None, TimeSpan.FromSeconds(1))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || time.Year is < 1900 or > 9998)
            yield return new ValidationResult("时间必须包含时区且精确到秒。", [field]);
    }
}
