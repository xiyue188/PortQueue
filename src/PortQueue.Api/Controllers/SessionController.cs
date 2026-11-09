using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortQueue.Api.Security;
using PortQueue.Application.Accounts;

namespace PortQueue.Api.Controllers;

/// <summary>Cookie 会话入口；返回公开身份和 CSRF 令牌，绝不返回认证存储记录。</summary>
[ApiController]
[Route("api/session")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SessionController(IAccountStore accounts, PasswordVerifier passwords, IAntiforgery antiforgery, IHostEnvironment environment, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public ActionResult<SessionResponse> Get() => Ok(Snapshot());

    // 仅展示实际存在且仍使用公开初始密码的账号；改密/停用后退出快捷列表。
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        if (!DemoAccounts.Enabled(environment, config)) return Ok(new { accounts = Array.Empty<object>(), defaultPassword = (string?)null });
        var options = new List<object>();
        foreach (var preset in DemoAccounts.Presets)
        {
            var account = await accounts.FindAsync(preset.Username, null, cancellationToken);
            if (account is not null && account.IsEnabled && account.RoleCode == preset.RoleCode
                && passwords.Verify(account, DemoAccounts.DefaultPassword))
                options.Add(new { account.Username, account.DisplayName, account.RoleCode, roleName = SessionSecurity.RoleName(account.RoleCode) });
        }
        return Ok(new { accounts = options, defaultPassword = options.Count > 0 ? DemoAccounts.DefaultPassword : null });
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<SessionResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!await ValidToken()) return CsrfFailure();
        var account = await accounts.FindAsync(request.Username.Trim(), null, cancellationToken);
        var validPassword = passwords.Verify(account, request.Password);
        if (!DemoAccounts.Enabled(environment, config) && DemoAccounts.Presets.Any(preset => string.Equals(preset.Username, request.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
            validPassword = false; // 演示专用身份在生产/关闭开关时同样不能登录。
        if (!validPassword || account is null || !account.IsEnabled || !SessionSecurity.Roles.Contains(account.RoleCode))
            return Failure(401, "用户名或密码错误，或账号不可用。", "INVALID_CREDENTIALS");
        var principal = SessionSecurity.Principal(account);
        await HttpContext.SignInAsync(SessionSecurity.Scheme, principal, new AuthenticationProperties { IsPersistent = false });
        HttpContext.User = principal;
        return Ok(Snapshot());
    }

    [HttpPost("logout")]
    public async Task<ActionResult<SessionResponse>> Logout()
    {
        if (!await ValidToken()) return CsrfFailure();
        await HttpContext.SignOutAsync(SessionSecurity.Scheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        return Ok(Snapshot());
    }
    private async Task<bool> ValidToken()
    {
        try { await antiforgery.ValidateRequestAsync(HttpContext); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
    private static ObjectResult CsrfFailure() => Failure(400, "会话校验已失效，请重试。", "INVALID_CSRF_TOKEN");
    private static ObjectResult Failure(int status, string title, string code) => new(new ProblemDetails
    { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
    private SessionResponse Snapshot()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var identity = User.Identity?.IsAuthenticated == true ? new SessionIdentity(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!, User.FindFirstValue("username")!,
            User.Identity.Name!, role!, SessionSecurity.RoleName(role!)) : null;
        return new(identity, new(identity is not null && role is "ADMIN" or "DISPATCHER",
            identity is not null && role is "ADMIN" or "REVIEWER"), antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);
    }
}

public sealed record LoginRequest(
    [Required(ErrorMessage = "请输入用户名。"), StringLength(50, ErrorMessage = "用户名不能超过50个字符。"), RegularExpression(@"[A-Za-z0-9._-]+", ErrorMessage = "用户名只支持字母、数字、点、下划线和短横线。")] string Username,
    [Required(ErrorMessage = "请输入密码。"), StringLength(128, ErrorMessage = "密码不能超过128个字符。")] string Password);
public sealed record SessionIdentity(string UserId, string Username, string DisplayName, string RoleCode, string RoleName);
public sealed record SessionPermissions(bool CanRegisterAppointment, bool CanReviewAppointment);
public sealed record SessionResponse(SessionIdentity? User, SessionPermissions Permissions, string CsrfToken);
