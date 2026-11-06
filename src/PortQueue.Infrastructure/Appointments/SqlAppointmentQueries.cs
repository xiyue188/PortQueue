using Dapper;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Appointments;

/// <summary>执行仓库维护的预约 SQL；连接逐请求释放，取消信号贯穿连接与命令。</summary>
public sealed class SqlAppointmentQueries(ISqlConnectionFactory connectionFactory) : IAppointmentQueries
{
    private static readonly string QuerySql = LoadSql();

    public async Task<AppointmentPage> GetPageAsync(AppointmentQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(QuerySql, new
        {
            query.Status,
            Pattern = query.Keyword is null ? null : $"%{EscapeLike(query.Keyword)}%",
            Offset = ((long)query.Page - 1) * query.PageSize,
            query.PageSize
        }, commandTimeout: 8, cancellationToken: cancellationToken);
        var rows = (await connection.QueryAsync<QueryRow>(command)).AsList();

        // OUTER APPLY 的空页哨兵只有总数，不能映射成一条虚假的空预约。
        var items = rows.Where(row => row.AppointmentId is not null).Select(row => new AppointmentListItem(
            row.AppointmentId!, row.AppointmentNo!, row.ShipName!, row.ImoNo, row.Mmsi,
            row.RequestedArrivalAt!.Value, row.RequestedDepartureAt!.Value,
            row.AppointmentStatusCode!, row.ReviewerName, row.ReviewedAt, row.ReviewComment)).ToArray();
        return new AppointmentPage(items, query.Page, query.PageSize, rows[0].TotalCount);
    }

    // 参数化解决注入；转义另外保证用户输入的通配符按普通字符匹配。
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);

    private static string LoadSql()
    {
        using var stream = typeof(SqlAppointmentQueries).Assembly
            .GetManifestResourceStream("PortQueue.Queries.Appointments.sql")
            ?? throw new InvalidOperationException("Embedded appointment query is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>SQL 外连接行：分页没有数据时，除总数外的列均可空。</summary>
    private sealed class QueryRow
    {
        public long TotalCount { get; init; }
        public string? AppointmentId { get; init; }
        public string? AppointmentNo { get; init; }
        public string? ShipName { get; init; }
        public string? ImoNo { get; init; }
        public string? Mmsi { get; init; }
        public DateTimeOffset? RequestedArrivalAt { get; init; }
        public DateTimeOffset? RequestedDepartureAt { get; init; }
        public string? AppointmentStatusCode { get; init; }
        public string? ReviewerName { get; init; }
        public DateTimeOffset? ReviewedAt { get; init; }
        public string? ReviewComment { get; init; }
    }
}
