using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using PortQueue.Application.Anchorages;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Anchorages;

/// <summary>每次查询是一条 SELECT，设施、队列及事实时间线使用同一数据库语句快照。</summary>
public sealed class SqlAnchorageQueries(ISqlConnectionFactory connections, IConfiguration configuration) : IAnchorageQueries
{
    private static readonly string ListSql = Load("Anchorages");
    private static readonly string QueueSql = Load("AnchorageQueue");
    private static readonly string StaySql = Load("AnchorageStay");
    private static readonly string CandidatesSql = Load("AnchorageEntryCandidates");
    private string HomePortCode => configuration["Port:Code"] ?? "SH-YS";

    public Task<AnchorageOverview?> GetAsync(long? appointmentId, CancellationToken cancellationToken)
    {
        if (appointmentId.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appointmentId.Value);
        return ReadAsync<AnchorageOverview>(ListSql, new { HomePortCode, AppointmentId = appointmentId }, cancellationToken);
    }

    public async Task<AnchorageQueueSnapshot?> GetQueueAsync(long anchorageId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(anchorageId);
        var result = await ReadAsync<QueueResult>(QueueSql, new { HomePortCode, AnchorageId = anchorageId }, cancellationToken);
        if (result is null) return null;
        var items = result.Items.Where(item => item.StatusCode is "WAITING" or "ASSIGNED").ToArray();
        var cancelled = result.Items.Where(item => item.StatusCode == "CANCELLED").ToArray();
        // 4D 在调度锁内用同样的 nvarchar(MAX) 串执行 HASHBYTES('SHA2_256', ...)；
        // SQL nvarchar 对应 UTF-16LE，无 BOM，空队列对空串哈希。顺序来自库内顺位与主键。
        var canonical = string.Join('|', items.Select(item =>
            $"{item.QueueEntryId}:{item.QueuePosition!.Value.ToString(CultureInfo.InvariantCulture)}:{item.StatusCode}"));
        var snapshot = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(canonical)));
        return new AnchorageQueueSnapshot(result.AnchorageId, result.ObservedAt, snapshot,
            result.OccupiedCount, result.Capacity, result.StatusCode, items, cancelled);
    }

    public Task<AnchorageStayDetail?> GetStayAsync(long queueEntryId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueEntryId);
        return ReadAsync<AnchorageStayDetail>(StaySql, new { HomePortCode, QueueEntryId = queueEntryId }, cancellationToken);
    }

    public async Task<AnchorageCandidatePage> GetEntryCandidatesAsync(string keyword, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<CandidateResult>(new CommandDefinition(CandidatesSql,
            new { Keyword = keyword.Trim(), Offset = ((long)page - 1) * pageSize, PageSize = pageSize }, commandTimeout: 8, cancellationToken: cancellationToken));
        return new AnchorageCandidatePage(JsonSerializer.Deserialize<AnchorageEntryCandidate[]>(result.ItemsJson)!, page, pageSize, result.TotalCount);
    }

    private async Task<T?> ReadAsync<T>(string sql, object parameters, CancellationToken cancellationToken) where T : class
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var json = await connection.QuerySingleAsync<string?>(new CommandDefinition(sql, parameters,
            commandTimeout: 8, cancellationToken: cancellationToken));
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("Anchorage query payload is missing.");
    }

    private static string Load(string name)
    {
        using var stream = typeof(SqlAnchorageQueries).Assembly.GetManifestResourceStream($"PortQueue.Queries.{name}.sql")
            ?? throw new InvalidOperationException("Embedded anchorage query is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record QueueResult(string AnchorageId, DateTimeOffset ObservedAt, int OccupiedCount,
        int? Capacity, string StatusCode, AnchorageQueueItem[] Items);
    private sealed record CandidateResult(long TotalCount, string ItemsJson);
}
