using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using PortQueue.Application.Berths;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Berths;

/// <summary>每份返回值由单条 SELECT 取得，资源、版本及事件不会混用不同读取时刻。</summary>
public sealed class SqlBerthQueries(ISqlConnectionFactory connections, IConfiguration configuration) : IBerthQueries
{
    private static readonly string ScheduleSql = Load("BerthSchedule");
    private static readonly string VisitsSql = Load("BerthVisits");
    private static readonly string VisitSql = Load("BerthVisit");
    private string HomePortCode => configuration["Port:Code"] ?? "SH-YS";

    public Task<BerthSchedule?> GetScheduleAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken)
    {
        if (rangeEnd <= rangeStart || rangeEnd - rangeStart > TimeSpan.FromHours(48)) throw new ArgumentOutOfRangeException(nameof(rangeEnd));
        return ReadAsync<BerthSchedule>(ScheduleSql, new { HomePortCode, FromUtc = rangeStart.UtcDateTime, ToUtc = rangeEnd.UtcDateTime }, cancellationToken);
    }

    public async Task<BerthVisitPage> GetVisitsAsync(string scope, string keyword, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (scope is not ("waiting" or "all")) throw new ArgumentException("Unknown visit scope.", nameof(scope));
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        if (keyword.Length > 100) throw new ArgumentException("Keyword is too long.", nameof(keyword));
        return await ReadAsync<BerthVisitPage>(VisitsSql, new
        {
            HomePortCode, Scope = scope, Keyword = keyword.Trim(), Page = page, PageSize = pageSize, Offset = ((long)page - 1) * pageSize
        }, cancellationToken) ?? throw new InvalidOperationException("Berth visits payload is missing.");
    }

    public Task<BerthVisitDetail?> GetVisitAsync(long appointmentId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appointmentId);
        return ReadAsync<BerthVisitDetail>(VisitSql, new { HomePortCode, AppointmentId = appointmentId }, cancellationToken);
    }

    private async Task<T?> ReadAsync<T>(string sql, object parameters, CancellationToken cancellationToken) where T : class
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var json = await connection.QuerySingleAsync<string?>(new CommandDefinition(sql, parameters,
            commandTimeout: 8, cancellationToken: cancellationToken));
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("Berth query payload is missing.");
    }

    private static string Load(string name)
    {
        using var stream = typeof(SqlBerthQueries).Assembly.GetManifestResourceStream($"PortQueue.Queries.{name}.sql")
            ?? throw new InvalidOperationException("Embedded berth query is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
