using PortQueue.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using PortQueue.Api.Errors;

// 启动组合根只负责组装基础设施和 HTTP 管道，业务规则放在应用层与数据库过程。
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // 自动模型绑定失败（如 page=abc）与业务参数错误采用相同契约。
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "查询条件无效，请检查后重试。",
            Type = "about:blank"
        };
        problem.Extensions["code"] = "INVALID_QUERY";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        var response = new BadRequestObjectResult(problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    };
});
builder.Services.AddHealthChecks();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddPortQueueInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
// 本地 HTTP 配置用于课程演示，避免没有 HTTPS 端口时产生误导性警告。
// 部署到 HTTPS 环境时，通过配置重新开启重定向。
if (app.Configuration.GetValue<bool>("EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}
app.UseAuthorization();

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
