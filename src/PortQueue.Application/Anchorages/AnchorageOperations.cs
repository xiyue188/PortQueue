namespace PortQueue.Application.Anchorages;

public interface IAnchorageOperations
{
    Task<AnchorageEventResult> RecordAsync(long? appointmentId, long? anchorageId, long? queueEntryId,
        string eventType, DateTimeOffset eventTime, string? remark, long actor, CancellationToken cancellationToken);
    Task<AnchorageEstimateResult> SetEstimateAsync(long queueEntryId, DateTimeOffset? estimatedAt,
        long expectedVersion, string reason, long actor, CancellationToken cancellationToken);
    Task<AnchorageReorderResult> ReorderAsync(long queueEntryId, int newPosition, string reason,
        string expectedSnapshot, long actor, CancellationToken cancellationToken);
}

public sealed record AnchorageEventResult(string QueueEntryId, string AnchorageId, string AppointmentId,
    string EventId, string EventTypeCode, DateTimeOffset EventTime);
public sealed record AnchorageEstimateResult(string QueueEntryId, DateTimeOffset? ManualEstimatedBerthingAt, string EstimateVersion);
public sealed record AnchorageReorderResult(string QueueEntryId, int OldPosition, int NewPosition, int AffectedCount, string Snapshot);
public sealed record AnchorageEntryCandidate(string AppointmentId, string AppointmentNo, string PortCallId,
    string CallNo, string ShipId, string ShipName, DateTimeOffset RequestedArrivalAt, byte PriorityLevel);
public sealed record AnchorageCandidatePage(IReadOnlyList<AnchorageEntryCandidate> Items, int Page, int PageSize, long TotalCount);
public sealed class AnchorageOperationException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
