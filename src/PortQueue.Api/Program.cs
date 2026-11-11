using PortQueue.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Api.Errors;
using PortQueue.Api.Security;
using PortQueue.Api.Commands;

// 启动组合根只负责组装基础设施和 HTTP 管道，业务规则放在应用层与数据库过程。
var builder = WebApplication.CreateBuilder(args.FirstOrDefault() == "account" ? [] : args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
    parts.FeatureProviders.Add(new PortQueue.Api.Mobile.MobileControllerFeatureProvider(builder.Configuration)))
    .ConfigureApiBehaviorOptions(options =>
{
    // 自动模型绑定失败（如 page=abc）与业务参数错误采用相同契约。
    options.InvalidModelStateResponseFactory = context =>
    {
        var isLogin = context.HttpContext.Request.Path.StartsWithSegments("/api/session/login");
        var isQuery = HttpMethods.IsGet(context.HttpContext.Request.Method);
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = isLogin ? "请填写有效的用户名和密码。" : isQuery ? "查询条件无效，请检查后重试。" : "提交内容无效，请检查后重试。",
            Type = "about:blank"
        };
        problem.Extensions["code"] = isLogin ? "INVALID_LOGIN_INPUT" : isQuery ? "INVALID_QUERY" : "INVALID_INPUT";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        var response = new BadRequestObjectResult(problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    };
});
builder.Services.AddHealthChecks();
builder.Services.AddPortQueueSession(builder.Environment.IsDevelopment());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddPortQueueInfrastructure(builder.Configuration);

var app = builder.Build();

// 管理入口不启动 Web 监听，也不把密码作为命令行参数。
if (args.FirstOrDefault() == "account")
{
    Environment.ExitCode = await AccountCommand.RunAsync(args.Skip(1).ToArray(), app.Services);
    await app.DisposeAsync();
    return;
}

app.UseExceptionHandler();
// 本地 HTTP 配置用于课程演示，避免没有 HTTPS 端口时产生误导性警告。
// 部署到 HTTPS 环境时，通过配置重新开启重定向。
if (app.Configuration.GetValue<bool>("EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

// 根路径用于本地启动确认，避免用户打开 API 端口时只看到无说明的 404。
// 业务接口仍按 /api/* 提供，健康检查仍使用 /api/health。
app.MapGet("/", () => Results.Ok(new
{
    service = "PortQueue.Api",
    status = "running",
    health = "/api/health",
    environment = app.Environment.EnvironmentName
}));

app.Run();

public partial class Program;
