using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Api.Security;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Controllers;

/// <summary>登记HTTP边界；客户端只提交业务内容，角色、防伪及操作者由服务器确定。</summary>
[ApiController, Route("api/appointments"), Authorize(Policy = SessionSecurity.RegisterPolicy)]
public sealed class AppointmentRegistrationController(IAppointmentRegistration registration, IAntiforgery antiforgery) : ControllerBase
{
    private static ObjectResult Error(int status, string title, string code) => new(new ProblemDetails { Status = status, Title = title, Extensions = { { "code", code } } }) { StatusCode = status };
    [HttpPost]
    public Task<IActionResult> Create(CreateAppointmentRequest request, CancellationToken token) =>
        Execute("CREATE", request.Data.ToData(), request.RequestKey, null, null, request.Submit, token);
    [HttpPut("{id}/draft")]
    public Task<IActionResult> Edit([Range(typeof(long), "1", "9223372036854775807")] long id, EditAppointmentRequest request, CancellationToken token) =>
        Execute("EDIT", request.Data.ToData(), null, id, long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture), false, token);
    [HttpPost("{id}/submit")]
    public Task<IActionResult> Submit([Range(typeof(long), "1", "9223372036854775807")] long id, SubmitAppointmentRequest request, CancellationToken token) =>
        Execute("SUBMIT", null, null, id, long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture), false, token);
    private async Task<IActionResult> Execute(string action, AppointmentRegistration? data, Guid? key, long? id, long? version, bool submit, CancellationToken token)
    {
        try { await antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException) { return Error(400, "会话已变化，请刷新会话后重试。", "INVALID_CSRF_TOKEN"); }
        if (action == "CREATE" && key == Guid.Empty) return Problem(statusCode: 400, title: "登记请求标识无效。");
        try
        {
            var result = await registration.SaveAsync(action, long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), data, key, id, version, submit, token);
            return StatusCode(action == "CREATE" && !result.Replayed ? 201 : 200, result);
        }
        catch (RegistrationException ex) { return Error(ex.Status, ex.Message, ex.Code); }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAppointmentRequest(Guid RequestKey, [Required] RegistrationInput Data, bool Submit);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EditAppointmentRequest([Required, RegularExpression(@"^(0|[1-9]\d{0,18})$"), Range(typeof(long), "0", "9223372036854775807")] string ExpectedVersion, [Required] RegistrationInput Data);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitAppointmentRequest([Required, RegularExpression(@"^(0|[1-9]\d{0,18})$"), Range(typeof(long), "0", "9223372036854775807")] string ExpectedVersion);

/// <summary>显式时区且精确到秒；拒绝静默四舍五入。日期字符串验证后才转换UTC。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationInput(
    [Required, RegularExpression(@"^[1-9]\d{0,18}$"), Range(typeof(long), "1", "9223372036854775807")] string ShipId,
    [RegularExpression(@"^[1-9]\d{0,18}$"), Range(typeof(long), "1", "9223372036854775807")] string? PreviousPortId,
    [RegularExpression(@"^[1-9]\d{0,18}$"), Range(typeof(long), "1", "9223372036854775807")] string? NextPortId,
    [Required] string PlannedArrivalAt, string? PlannedDepartureAt,
    [Range(0, int.MaxValue)] int? CargoTeu, [Range(typeof(decimal), "0", "9999999999.99")] decimal? CargoWeightT,
    [Required] string RequestedArrivalAt, [Required] string RequestedDepartureAt,
    [Range(typeof(decimal), "0.01", "9999.99")] decimal EstimatedOperationHours,
    [Range(typeof(decimal), "0.01", "9999.99")] decimal DeclaredDraftM,
    [Range(1, 5)] byte PriorityLevel, bool DangerousGoodsFlag, [StringLength(1000)] string? SpecialRequirement) : IValidatableObject
{
    private static bool ValidTime(string? value) => value is not null && Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.0{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.None, TimeSpan.FromSeconds(1))
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) && time.Year >= 1900 && time.Year <= 9998;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, value) in new[] { (nameof(PlannedArrivalAt), PlannedArrivalAt), (nameof(RequestedArrivalAt), RequestedArrivalAt), (nameof(RequestedDepartureAt), RequestedDepartureAt) })
            if (!ValidTime(value)) yield return new ValidationResult("时间必须包含时区且精确到秒。", [name]);
        if (PlannedDepartureAt is not null && !ValidTime(PlannedDepartureAt)) yield return new ValidationResult("计划离港时间无效。", [nameof(PlannedDepartureAt)]);
        foreach (var (name, value) in new[] { (nameof(CargoWeightT), CargoWeightT), (nameof(EstimatedOperationHours), (decimal?)EstimatedOperationHours), (nameof(DeclaredDraftM), (decimal?)DeclaredDraftM) })
            if (value is not null && decimal.Round(value.Value, 2) != value) yield return new ValidationResult("最多保留两位小数。", [name]);
    }
    public AppointmentRegistration ToData() => new(ShipId, PreviousPortId, NextPortId, Parse(PlannedArrivalAt), PlannedDepartureAt is null ? null : Parse(PlannedDepartureAt), CargoTeu, CargoWeightT,
        Parse(RequestedArrivalAt), Parse(RequestedDepartureAt), EstimatedOperationHours, DeclaredDraftM, PriorityLevel, DangerousGoodsFlag, string.IsNullOrWhiteSpace(SpecialRequirement) ? null : SpecialRequirement.Trim());
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).ToUniversalTime();
}
