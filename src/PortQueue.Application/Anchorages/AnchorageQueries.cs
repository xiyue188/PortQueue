namespace PortQueue.Application.Anchorages;

public interface IAnchorageQueries
{
    Task<AnchorageOverview?> GetAsync(long? appointmentId, CancellationToken cancellationToken);
    Task<AnchorageQueueSnapshot?> GetQueueAsync(long anchorageId, CancellationToken cancellationToken);
    Task<AnchorageStayDetail?> GetStayAsync(long queueEntryId, CancellationToken cancellationToken);
    Task<AnchorageCandidatePage> GetEntryCandidatesAsync(string keyword, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record AnchorageOverview(DateTimeOffset ObservedAt, string PortId, string PortCode,
    string PortName, LocatedAnchorageStay? LocatedStay, IReadOnlyList<AnchorageSummary> Items);

public sealed record LocatedAnchorageStay(string QueueEntryId, string AnchorageId);

public sealed record AnchorageSummary(string AnchorageId, string Code, string Name, string StatusCode,
    int? Capacity, long OccupiedCount, long QueueCount);

public sealed record AnchorageQueueSnapshot(string AnchorageId, DateTimeOffset ObservedAt, string Snapshot,
    int OccupiedCount, int? Capacity, string StatusCode,
    IReadOnlyList<AnchorageQueueItem> Items, IReadOnlyList<AnchorageQueueItem> Cancelled);

/// <summary>每项对应一次停留；旧预计不代表人工预计，也不能替代已确认计划。</summary>
public class AnchorageQueueItem
{
    public string QueueEntryId { get; init; } = string.Empty;
    public string AnchorageId { get; init; } = string.Empty;
    public string AppointmentId { get; init; } = string.Empty;
    public string PortCallId { get; init; } = string.Empty;
    public string CallNo { get; init; } = string.Empty;
    public string AppointmentNo { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public int? QueuePosition { get; init; }
    public byte PriorityLevel { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public DateTimeOffset? EnteredAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public DateTimeOffset? LegacyEstimatedBerthingAt { get; init; }
    public DateTimeOffset? ManualEstimatedBerthingAt { get; init; }
    public string EstimateVersion { get; init; } = "0";
    public DateTimeOffset? ConfirmedBerthingAt { get; init; }
    public DateTimeOffset? ConfirmedUnberthingAt { get; init; }
    public string? BerthName { get; init; }
    public string? ConfirmedRevisionId { get; init; }
}

public sealed class AnchorageStayDetail : AnchorageQueueItem
{
    // 确认操作时间与预计执行时间分别返回，前者才能构成已发生的时间线节点。
    public DateTimeOffset? PlanConfirmedAt { get; init; }
    public string AnchorageName { get; init; } = string.Empty;
    public DateTimeOffset RequestedArrivalAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewerName { get; init; }
    public string AppointmentStatusCode { get; init; } = string.Empty;
    public IReadOnlyList<AnchorageVisitEvent> Events { get; init; } = [];
}

public sealed record AnchorageVisitEvent(string EventId, string EventTypeCode, DateTimeOffset EventTime,
    string? QueueEntryId, string? RecordedByName);
