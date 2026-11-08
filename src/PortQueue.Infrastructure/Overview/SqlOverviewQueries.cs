using System.Text.Json;
using Dapper;
using PortQueue.Application.Overview;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Overview;

/// <summary>复用已有视图，以单条 SELECT 保证摘要与事件来自同一语句读取快照。</summary>
public sealed class SqlOverviewQueries(ISqlConnectionFactory connectionFactory) : IOverviewQueries
{
    private static readonly string QuerySql = LoadSql();

    public async Task<PortOverview> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(QuerySql, commandTimeout: 8,
            cancellationToken: cancellationToken);
        var counts = await connection.QuerySingleAsync<OverviewCounts>(command);
        var events = JsonSerializer.Deserialize<RecentPortEvent[]>(counts.RecentEventsJson)
            ?? throw new InvalidOperationException("Overview event payload is missing.");
        return new PortOverview(counts.PendingAppointments, counts.AnchorageVessels, counts.ConfirmedPlans, events);
    }

    private static string LoadSql()
    {
        using var stream = typeof(SqlOverviewQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.Overview.sql")
            ?? throw new InvalidOperationException("Embedded overview query is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class OverviewCounts
    {
        public long PendingAppointments { get; init; }
        public long AnchorageVessels { get; init; }
        public long ConfirmedPlans { get; init; }
        public string RecentEventsJson { get; init; } = "[]";
    }
}
