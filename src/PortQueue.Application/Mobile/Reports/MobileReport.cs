namespace PortQueue.Application.Mobile.Reports;
public sealed record LocationSample(decimal Latitude,
    decimal Longitude,
    double AccuracyMeters,
    DateTimeOffset CapturedAt,
    string CoordinateSystem);
public sealed record PhotoMetadata(Guid RequestId,
    string AppointmentId,
    string Category,
    string Description,
    DateTimeOffset CapturedAt,
    LocationSample? Location);
public sealed record EtaMetadata(Guid RequestId,
    string AppointmentId,
    DateTimeOffset ProposedEta,
    string Description);
public record ReportSummary(string ReportId,
    string RequestId,
    string AppointmentId,
    string AppointmentNo,
    string CallNo,
    string ShipName,
    string ReportType,
    string? Category,
    string Description,
    string StatusCode,
    DateTimeOffset SubmittedAt,
    string SubmittedByUserId,
    string SubmittedByName,
    DateTimeOffset? ProposedEta);
public sealed record PhotoInfo(string PhotoId,
    string MimeType,
    long ByteSize,
    DateTimeOffset CapturedAt,
    LocationSample? Location);
public sealed record ReportDetail(string ReportId,
    string RequestId,
    string AppointmentId,
    string AppointmentNo,
    string CallNo,
    string ShipName,
    string ReportType,
    string? Category,
    string Description,
    string StatusCode,
    DateTimeOffset SubmittedAt,
    string SubmittedByUserId,
    string SubmittedByName,
    DateTimeOffset? ProcessedAt,
    string? ProcessedByName,
    string? ProcessComment,
    string Version,
    bool CanProcess,
    PhotoInfo? Photo,
    DateTimeOffset? ProposedEta) : ReportSummary(ReportId,
    RequestId,
    AppointmentId,
    AppointmentNo,
    CallNo,
    ShipName,
    ReportType,
    Category,
    Description,
    StatusCode,
    SubmittedAt,
    SubmittedByUserId,
    SubmittedByName,
    ProposedEta);
public sealed record ReportPage(IReadOnlyList<ReportSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);
public sealed record ReportActor(long UserId,
    string Role)
{
    public bool CanSubmit => Role is "ADMIN" or "DISPATCHER" or "RECORDER";
    public bool CanProcess => Role is "ADMIN" or "DISPATCHER";
}
public sealed record ReportSubmission(ReportDetail Report,
    bool IsReplay);
public sealed record ProtectedPhoto(string Path);
public sealed class MobileReportException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public interface IMobileReportService
{
    Task<ReportSubmission> SubmitPhotoAsync(ReportActor actor, PhotoMetadata metadata, Stream photo, CancellationToken token);
    Task<ReportSubmission> SubmitEtaAsync(ReportActor actor, EtaMetadata metadata, CancellationToken token);
    Task<ReportDetail?> GetAsync(ReportActor actor, long reportId, CancellationToken token);
    Task<ReportDetail?> FindByRequestAsync(ReportActor actor, Guid requestId, CancellationToken token);
    Task<ReportPage> ListAsync(ReportActor actor, string scope, int page, int pageSize, long? appointmentId, string? status, CancellationToken token);
    Task<ProtectedPhoto?> PhotoAsync(ReportActor actor, long photoId, CancellationToken token);
    Task<ReportDetail> DecideAsync(ReportActor actor, long reportId, string decision, string comment, string expectedVersion, CancellationToken token);
}
