using Dapper;
using PortQueue.Application.Mobile;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Mobile;

/// <summary>执行 database/ 中的单条参数化查询，不写核心业务状态。</summary>
public sealed class SqlMobileVisitQueries(ISqlConnectionFactory connections) : IMobileVisitQueries
{
    private static readonly string Sql = LoadSql();

    private static string LoadSql()
    {
        using var stream = typeof(SqlMobileVisitQueries).Assembly
            .GetManifestResourceStream("PortQueue.Queries.MobileVisitOverview.sql")
            ?? throw new InvalidOperationException("Mobile visit query is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<VisitOverview?> GetAsync(long appointmentId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appointmentId);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Sql, new { AppointmentId = appointmentId }, commandTimeout: 8, cancellationToken: cancellationToken));
        if (row is null) return null;

        var queue = row.AnchorageName is null ? null : new VisitQueueEntry(
            row.AnchorageName, row.QueuePosition, row.QueueStatusCode!, row.EnteredAt, row.LeftAt);
        var plan = row.BerthName is null ? null : new PublishedVisitPlan(
            row.BerthName, row.VersionNo!.Value, row.PlanStatusCode!,
            row.PlannedBerthingAt!.Value, row.PlannedUnberthingAt!.Value);
        return new VisitOverview(
            row.AppointmentId, row.AppointmentNo, row.ShipName, row.CallNo,
            row.AppointmentStatusCode, row.CallStatusCode, row.PlannedArrivalAt,
            queue, plan, null, false, row.ServerTime);
    }

    private sealed class Row
    {
        public string AppointmentId { get; init; } = "";
        public string AppointmentNo { get; init; } = "";
        public string ShipName { get; init; } = "";
        public string CallNo { get; init; } = "";
        public string AppointmentStatusCode { get; init; } = "";
        public string CallStatusCode { get; init; } = "";
        public DateTimeOffset PlannedArrivalAt { get; init; }
        public string? AnchorageName { get; init; }
        public int? QueuePosition { get; init; }
        public string? QueueStatusCode { get; init; }
        public DateTimeOffset? EnteredAt { get; init; }
        public DateTimeOffset? LeftAt { get; init; }
        public string? BerthName { get; init; }
        public int? VersionNo { get; init; }
        public string? PlanStatusCode { get; init; }
        public DateTimeOffset? PlannedBerthingAt { get; init; }
        public DateTimeOffset? PlannedUnberthingAt { get; init; }
        public DateTimeOffset ServerTime { get; init; }
    }
}
