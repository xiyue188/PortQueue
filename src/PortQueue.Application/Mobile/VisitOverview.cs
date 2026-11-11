namespace PortQueue.Application.Mobile;

public interface IMobileVisitQueries
{
    Task<VisitOverview?> GetAsync(long appointmentId, CancellationToken cancellationToken);
}

/// <summary>按预约定位唯一航次；时间统一为 UTC，M1 不提供报送写能力。</summary>
public sealed record VisitOverview(
    string AppointmentId,
    string AppointmentNo,
    string ShipName,
    string CallNo,
    string AppointmentStatusCode,
    string CallStatusCode,
    DateTimeOffset PlannedArrivalAt,
    VisitQueueEntry? Queue,
    PublishedVisitPlan? PublishedPlan,
    AcceptedVisitEta? LatestAcceptedEta,
    bool CanSubmitReport,
    DateTimeOffset ServerTime);

public sealed record VisitQueueEntry(
    string AnchorageName,
    int? QueuePosition,
    string StatusCode,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? LeftAt);

public sealed record PublishedVisitPlan(
    string BerthName,
    int VersionNo,
    string StatusCode,
    DateTimeOffset PlannedBerthingAt,
    DateTimeOffset PlannedUnberthingAt);

public sealed record AcceptedVisitEta(
    string ReportId,
    DateTimeOffset ProposedEta,
    DateTimeOffset ProcessedAt);
