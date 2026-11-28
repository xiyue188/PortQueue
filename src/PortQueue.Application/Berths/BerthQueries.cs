namespace PortQueue.Application.Berths;

public interface IBerthQueries
{
    Task<BerthSchedule?> GetScheduleAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken);
    Task<BerthVisitPage> GetVisitsAsync(string scope, string keyword, int page, int pageSize, CancellationToken cancellationToken);
    Task<BerthVisitDetail?> GetVisitAsync(long appointmentId, CancellationToken cancellationToken);
}

public sealed record BerthSchedule(DateTimeOffset ObservedAt, DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<BerthResource> Resources, IReadOnlyList<BerthRevision> Revisions, IReadOnlyList<BerthOccupancy> Occupancies);

public sealed record BerthResource(string BerthId, string Code, string Name, string TerminalName,
    string StatusCode, string TerminalStatusCode, string PortStatusCode, decimal LengthM, decimal MaxDraftM,
    decimal? MaxDwt, IReadOnlyList<BerthShipType> ShipTypes);

public sealed record BerthShipType(string ItemId, string Name);

public sealed record BerthRevision(string RevisionId, string BerthPlanId, string AppointmentId, string ShipName,
    string CallNo, string BerthId, int VersionNo, DateTimeOffset PlannedBerthingAt, DateTimeOffset PlannedUnberthingAt,
    string PlanStatusCode, bool IsCurrent, DateTimeOffset? ConfirmedAt, DateTimeOffset RevisedAt,
    string RevisionReason, string RevisedByName, string? ConfirmedByName);

public sealed record BerthOccupancy(string AppointmentId, string ShipName, string CallNo, string BerthId,
    string ExecutedRevisionId, DateTimeOffset ActualBerthingAt, DateTimeOffset? ActualUnberthingAt);

public sealed record BerthVisitPage(IReadOnlyList<BerthVisitSummary> Items, int Page, int PageSize, long TotalCount);

public class BerthVisitSummary
{
    public string AppointmentId { get; init; } = string.Empty;
    public string AppointmentNo { get; init; } = string.Empty;
    public string PortCallId { get; init; } = string.Empty;
    public string CallNo { get; init; } = string.Empty;
    public string ShipId { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string AppointmentStatusCode { get; init; } = string.Empty;
    public DateTimeOffset RequestedArrivalAt { get; init; }
    public decimal EstimatedOperationHours { get; init; }
    public string ShipTypeItemId { get; init; } = string.Empty;
    public string ShipTypeName { get; init; } = string.Empty;
    public decimal LengthM { get; init; }
    public decimal DeclaredDraftM { get; init; }
    public decimal? DeadweightT { get; init; }
    public string? PlanStatusCode { get; init; }
    public string? BerthPlanId { get; init; }
    public string? QueueEntryId { get; init; }
    public string? AnchorageName { get; init; }
    public DateTimeOffset? ManualEstimatedBerthingAt { get; init; }
}

public sealed class BerthVisitDetail : BerthVisitSummary
{
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewerName { get; init; }
    public DateTimeOffset? EnteredAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public IReadOnlyList<BerthRevision> Versions { get; init; } = [];
    public IReadOnlyList<BerthVisitEvent> Events { get; init; } = [];
}

public sealed record BerthVisitEvent(string EventId, string EventTypeCode, DateTimeOffset EventTime,
    string? QueueEntryId, string? ExecutedRevisionId, string? RecordedByName);
