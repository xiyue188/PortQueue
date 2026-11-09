using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Application.Accounts;

namespace PortQueue.Api.Security;

/// <summary>单港单角色授权；写接口复用策略，不能依赖前端隐藏按钮。</summary>
public static class SessionSecurity
{
    public const string Scheme = "PortQueue";
    public const string RegisterPolicy = "Appointment.Register";
    public const string ReviewPolicy = "Appointment.Review";
    public static readonly string[] Roles = ["ADMIN", "DISPATCHER", "REVIEWER", "RECORDER"];
    public static string RoleName(string role) => role switch
    {
        "ADMIN" => "管理员", "DISPATCHER" => "调度员", "REVIEWER" => "审核员", "RECORDER" => "记录员", _ => ""
    };
    public static string Stamp(Account account) => Convert.ToHexString(SHA256.HashData(account.PasswordHash));
    public static ClaimsPrincipal Principal(Account account) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, account.UserId.ToString(CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, account.DisplayName), new Claim(ClaimTypes.Role, account.RoleCode),
        new Claim("username", account.Username), new Claim("password_stamp", Stamp(account))], Scheme));

    public static void AddPortQueueSession(this IServiceCollection services, bool development)
    {
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
        services.AddSingleton<PasswordVerifier>();
        services.AddScoped<SessionCookieEvents>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "PortQueue.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "PortQueue.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.EventsType = typeof(SessionCookieEvents);
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(RegisterPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("ADMIN", "DISPATCHER"));
            options.AddPolicy(ReviewPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("ADMIN", "REVIEWER"));
        });
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                { Status = 429, Title = "登录尝试过于频繁，请一分钟后重试。", Extensions = { ["code"] = "LOGIN_RATE_LIMITED" } }, token);
            };
        });
    }
}

/// <summary>每个认证请求重查启停和角色；密码重置使旧 Cookie 失效。</summary>
public sealed class SessionCookieEvents(IAccountStore accounts) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = long.TryParse(idText, out var id) ? await accounts.FindAsync(null, id, context.HttpContext.RequestAborted) : null;
        if (account is null || !account.IsEnabled || !SessionSecurity.Roles.Contains(account.RoleCode)
            || context.Principal?.FindFirstValue("password_stamp") != SessionSecurity.Stamp(account))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(SessionSecurity.Scheme);
            return;
        }
        // 替换当前请求身份，不延长 Cookie 的绝对有效期。
        context.ReplacePrincipal(SessionSecurity.Principal(account));
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) => Reject(context, 401, "请先登录。", "UNAUTHENTICATED");
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) => Reject(context, 403, "当前账号没有此操作权限。", "FORBIDDEN");
    private static Task Reject(RedirectContext<CookieAuthenticationOptions> context, int status, string title, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } });
    }
}

/// <summary>标准哈希验证；未知账号及旧随机占位均进行一次有效的哈希比较，统一失败响应。</summary>
public sealed class PasswordVerifier(IPasswordHasher<Account> hasher)
{
    private readonly Account _dummy = new();
    private readonly string _dummyHash = hasher.HashPassword(new Account(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    public bool Verify(Account? account, string password)
    {
        if (account is null) { hasher.VerifyHashedPassword(_dummy, _dummyHash, password); return false; }
        try
        {
            var hash = Encoding.UTF8.GetString(account.PasswordHash);
            var bytes = Convert.FromBase64String(hash);
            // 项目只接收 PasswordHasher V3，避免旧随机占位被当作认证材料。
            if (bytes.Length != 61 || bytes[0] != 1) throw new FormatException();
            return hasher.VerifyHashedPassword(account, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            hasher.VerifyHashedPassword(_dummy, _dummyHash, password);
            return false;
        }
    }
}
