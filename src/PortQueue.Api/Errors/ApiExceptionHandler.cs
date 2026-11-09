using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace PortQueue.Api.Errors;

/// <summary>将服务端故障转为稳定错误响应；技术细节只进入服务端日志。</summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        // 连接不可达/超时与 SQL 编程错误分开；列名错误等不伪装成服务暂时离线。
        var unavailable = exception is SqlException sql && sql.Errors.Cast<SqlError>().Any(error =>
            error.Number is -2 or 0 or 2 or 53 or 64 or 233 or 258 or 4060 or 10053 or 10054 or 10060 or 10061 or 11001);
        LogFailure(logger, httpContext.TraceIdentifier, exception);
        var problem = new ProblemDetails
        {
            Status = unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
            Title = unavailable ? "数据服务暂时不可用，请稍后重试。" : "服务暂时无法完成请求。",
            Type = "about:blank"
        };
        problem.Extensions["code"] = unavailable ? "DATABASE_UNAVAILABLE" : "INTERNAL_ERROR";
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "API request failed. TraceId: {TraceId}")]
    private static partial void LogFailure(ILogger logger, string traceId, Exception exception);
}
