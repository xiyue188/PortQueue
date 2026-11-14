using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Mobile.Reports;
namespace PortQueue.Api.Mobile;
[ApiController, Authorize, Route("api/mobile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MobileReportsController(IMobileReportService reports, IAntiforgery csrf) : ControllerBase
{
    private ReportActor Actor => new(long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture), User.FindFirstValue(ClaimTypes.Role)!);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    [HttpPost("reports/photo"), Authorize(Roles = "ADMIN,DISPATCHER,RECORDER"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Submit(CancellationToken token)
    {
        return await Execute(async () =>
        {
            await csrf.ValidateRequestAsync(HttpContext);
            if (!Request.HasFormContentType)
                throw new MobileReportException(400, "INVALID_MULTIPART", "请附加照片和报送内容。");
            var form = await Request.ReadFormAsync(token);
            if (form.Files.Count != 1 || form.Files[0].Name != "photo" || form["metadata"].Count != 1 || form["metadata"].ToString().Length > 8192)
                throw new MobileReportException(400, "INVALID_MULTIPART", "请附加一张照片和报送内容。");
            PhotoMetadata metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<PhotoMetadata>(form["metadata"].ToString(), JsonOptions) ?? throw new JsonException();
            }
            catch (JsonException) { throw new MobileReportException(400, "INVALID_METADATA", "报送内容格式不正确。"); }
            await using var stream = form.Files[0].OpenReadStream();
            var result = await reports.SubmitPhotoAsync(Actor, metadata, stream, token);
            return StatusCode(result.IsReplay ? 200 : 201, result.Report);
        });
    }
    [HttpGet("reports")]
    public Task<IActionResult> List(string scope = "mine", int page = 1, int pageSize = 20, long? appointmentId = null, string? status = null, CancellationToken token = default) => Execute(async () => Ok(await reports.ListAsync(Actor, scope, page, pageSize, appointmentId, status, token)));
    [HttpPost("reports/eta"), Authorize(Roles = "ADMIN,DISPATCHER,RECORDER"), RequestSizeLimit(8192)]
    public Task<IActionResult> SubmitEta(EtaRequest request, CancellationToken token) => Execute(async () =>
    {
        await csrf.ValidateRequestAsync(HttpContext);
        var text = request.ProposedEta;
        // Require an explicit zone; never infer the API machine's local timezone.
        if (text.Length < 20 || !(text.EndsWith('Z') || text[^6] is '+' or '-') ||
            !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var eta))
            throw new MobileReportException(400, "INVALID_ETA", "预计到达时间须包含明确时区。");
        var result = await reports.SubmitEtaAsync(Actor, new(request.RequestId, request.AppointmentId, eta, request.Description), token);
        return StatusCode(result.IsReplay ? 200 : 201, result.Report);
    });
    [HttpGet("reports/by-request/{requestId:guid}")]
    public Task<IActionResult> Find(Guid requestId, CancellationToken token) => Execute(async () => Found(await reports.FindByRequestAsync(Actor, requestId, token)));
    [HttpGet("reports/{reportId:long}")]
    public Task<IActionResult> Get([Range(1, long.MaxValue)] long reportId, CancellationToken token) => Execute(async () => Found(await reports.GetAsync(Actor, reportId, token)));
    [HttpGet("photos/{photoId:long}")]
    public Task<IActionResult> Photo([Range(1, long.MaxValue)] long photoId, CancellationToken token) => Execute(async () =>
    {
        var p = await reports.PhotoAsync(Actor, photoId, token);
        if (p is null)
            return Found(null);
        // Open only after authorization; no physical storage keys enter the response.
        try
        {
            return File(new FileStream(p.Path, FileMode.Open, FileAccess.Read, FileShare.Read), "image/jpeg", enableRangeProcessing: false);
        }
        catch (FileNotFoundException) { return Found(null); }
    });
    [HttpPost("reports/{reportId:long}/decision"), Authorize(Roles = "ADMIN,DISPATCHER")]
    public Task<IActionResult> Decide([Range(1, long.MaxValue)] long reportId, DecisionRequest request, CancellationToken token) => Execute(async () =>
    {
        await csrf.ValidateRequestAsync(HttpContext);
        return Ok(await reports.DecideAsync(Actor, reportId, request.Decision, request.Comment, request.ExpectedVersion, token));
    });
    private IActionResult Found(ReportDetail? value) => value is null ? Error(404, "REPORT_NOT_FOUND", "记录不存在。") : Ok(value);
    private static async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (MobileReportException ex) { return Error(ex.Status, ex.Code, ex.Message); }
        catch (AntiforgeryValidationException) { return Error(400, "INVALID_CSRF_TOKEN", "会话已变化，请重新确认后提交。"); }
    }
    private static ObjectResult Error(int status, string code, string title) => new(new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecisionRequest([Required] string Decision, [Required, StringLength(500)] string Comment, [Required] string ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EtaRequest(Guid RequestId, [Required] string AppointmentId, [Required] string ProposedEta, [Required] string Description);
