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
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
