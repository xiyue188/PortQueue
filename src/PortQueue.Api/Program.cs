using PortQueue.Infrastructure;

// 启动组合根只负责组装基础设施和 HTTP 管道，业务规则放在应用层与数据库过程。
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddProblemDetails();
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

app.Run();

public partial class Program;
