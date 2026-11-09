using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Api.Security;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Controllers;

/// <summary>审核HTTP边界：鉴权、防伪造、字段验证；责任人与时间不接受客户端覆盖。</summary>
[ApiController, Route("api/appointments"), Authorize(Policy = SessionSecurity.ReviewPolicy)]
public sealed class AppointmentReviewController(IAppointmentReviews reviews, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("{appointmentId}/review")]
    public async Task<IActionResult> Review([Range(typeof(long), "1", "9223372036854775807")] long appointmentId,
        ReviewRequest request, CancellationToken cancellationToken)
    {
        try { await antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException) { return Error(400, "INVALID_CSRF_TOKEN", "会话已变化，请重新确认后提交。"); }
        var comment = string.IsNullOrWhiteSpace(request.ReviewComment) ? null : request.ReviewComment.Trim();
        if (request.Decision == "REJECTED" && comment is null) return Error(400, "REVIEW_REASON_REQUIRED", "驳回时请填写原因。");
        var reviewerId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        try
        {
            await reviews.ReviewAsync(appointmentId, reviewerId, request.Decision, comment, cancellationToken);
            // 已提交确认与详情读取分开；随后读取失败不能把成功审核误报为保存失败。
            return Ok(new { appointmentId = appointmentId.ToString(CultureInfo.InvariantCulture), appointmentStatusCode = request.Decision });
        }
        catch (AppointmentReviewException ex)
        {
            return Error(ex.Code switch { "APPOINTMENT_NOT_FOUND" => 404, "APPOINTMENT_STATE_CHANGED" or "REVIEW_BUSY" => 409, "REVIEW_FORBIDDEN" => 403, _ => 400 }, ex.Code, ex.Message);
        }
    }
    private static ObjectResult Error(int status, string code, string title) => new(new ProblemDetails
    { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}

// 未声明字段直接拒绝，不能通过 reviewerId/operatorId 等字段冒充操作者。
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewRequest(
    [Required, RegularExpression("^(APPROVED|REJECTED)$")] string Decision,
    [StringLength(1000)] string? ReviewComment);
