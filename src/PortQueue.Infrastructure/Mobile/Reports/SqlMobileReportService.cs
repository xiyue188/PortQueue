using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using PortQueue.Application.Mobile.Reports;
using PortQueue.Infrastructure.Database;
namespace PortQueue.Infrastructure.Mobile.Reports;
public sealed class SqlMobileReportService(ISqlConnectionFactory connections, ReportPhotoStore photos, IReportTransactionCommitter committer) : IMobileReportService
{
    private const string SelectSql = """
 SELECT CONVERT(varchar(20),r.report_id) ReportId,CONVERT(varchar(36),r.request_id) RequestId,
 CONVERT(varchar(20),a.appointment_id) AppointmentId,a.appointment_no AppointmentNo,c.call_no CallNo,s.ship_name ShipName,
 r.report_type ReportType,r.category Category,r.description Description,r.status_code StatusCode,TODATETIMEOFFSET(r.proposed_eta,'+00:00') ProposedEta,
 TODATETIMEOFFSET(r.submitted_at,'+00:00') SubmittedAt,CONVERT(varchar(20),r.submitted_by) SubmittedByUserId,u.display_name SubmittedByName,
 TODATETIMEOFFSET(r.processed_at,'+00:00') ProcessedAt,v.display_name ProcessedByName,r.process_comment ProcessComment,r.row_version Version,
 CONVERT(varchar(20),p.photo_id) PhotoId,p.mime_type MimeType,p.byte_size ByteSize,TODATETIMEOFFSET(p.captured_at,'+00:00') CapturedAt,
 p.latitude Latitude,p.longitude Longitude,p.accuracy_m AccuracyMeters,TODATETIMEOFFSET(p.location_at,'+00:00') LocationAt,p.coordinate_system CoordinateSystem,p.storage_key StorageKey
 FROM dbo.mobile_report r JOIN dbo.arrival_appointment a ON a.port_call_id=r.port_call_id
 JOIN dbo.port_call c ON c.port_call_id=r.port_call_id JOIN dbo.ship s ON s.ship_id=c.ship_id
 JOIN dbo.user_account u ON u.user_id=r.submitted_by LEFT JOIN dbo.user_account v ON v.user_id=r.processed_by
 LEFT JOIN dbo.report_photo p ON p.report_id=r.report_id
 WHERE (@All=1 OR r.submitted_by=@UserId)
 """;
    private static MobileReportException Forbidden() => new(403, "FORBIDDEN", "当前账号没有此操作权限。");
    private static MobileReportException Invalid() => new(400, "INVALID_REPORT", "请检查报送内容、拍摄时间和位置。");
    private static CommandDefinition Command(string sql, object args, SqlTransaction? tx, CancellationToken token, CommandType? type = null) => new(sql, args, tx, commandTimeout: 20, commandType: type, cancellationToken: token);
    private static async Task<Row?> ReadAsync(SqlConnection c, SqlTransaction? tx, ReportActor a, long id, CancellationToken token) => await c.QuerySingleOrDefaultAsync<Row>(Command(SelectSql + " AND r.report_id=@Id", new { All = a.CanProcess, UserId = a.UserId, Id = id }, tx, token));
    public async Task<ReportSubmission> SubmitPhotoAsync(ReportActor actor, PhotoMetadata metadata, Stream photo, CancellationToken token)
    {
        if (!actor.CanSubmit)
            throw Forbidden();
        if (metadata.RequestId == Guid.Empty || !long.TryParse(metadata.AppointmentId, out var appointmentId) || appointmentId <= 0 || metadata.Category is not ("SITE" or "ARRIVAL" or "DELAY") || string.IsNullOrWhiteSpace(metadata.Description) || metadata.Description.Trim().Length > 500 || metadata.CapturedAt == default)
            throw Invalid();
        var location = metadata.Location;
        if (location is not null && (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180 || !double.IsFinite(location.AccuracyMeters) || location.AccuracyMeters < 0 || location.CoordinateSystem != "WGS84" || location.CapturedAt == default))
            throw Invalid();
        metadata = metadata with
        {
            AppointmentId = appointmentId.ToString(CultureInfo.InvariantCulture),
            Description = metadata.Description.Trim(),
            CapturedAt = metadata.CapturedAt.ToUniversalTime(),
            Location = location is null ? null : location with
            {
                CapturedAt = location.CapturedAt.ToUniversalTime()
            }
        };
        var bytes = await ReportPhotoStore.ValidateAsync(photo, token);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            metadata,
            photoHash = Convert.ToHexString(SHA256.HashData(bytes))
        });
        var hash = SHA256.HashData(canonical);
        var key = Guid.NewGuid().ToString("N") + ".jpg";
        await photos.SaveAsync(key, bytes, token);
        var commitStarted = false;
        var rolledBack = false;
        await using var c = connections.Create();
        try
        {
            await c.OpenAsync(token);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(token);
            try
            {
                var receipt = await c.QuerySingleAsync<Receipt>(Command("dbo.usp_mobile_submit_photo", new
                {
                    user_id = actor.UserId,
                    request_id = metadata.RequestId,
                    appointment_id = appointmentId,
                    category = metadata.Category,
                    description = metadata.Description,
                    payload_hash = hash,
                    storage_key = key,
                    byte_size = bytes.LongLength,
                    captured_at = metadata.CapturedAt.UtcDateTime,
                    latitude = location?.Latitude,
                    longitude = location?.Longitude,
                    accuracy_m = location?.AccuracyMeters,
                    location_at = location?.CapturedAt.UtcDateTime,
                    coordinate_system = location?.CoordinateSystem
                }, tx, token, CommandType.StoredProcedure));
                var row = await ReadAsync(c, tx, actor, receipt.ReportId, token) ?? throw new InvalidOperationException("Receipt missing.");
                commitStarted = true;
                await committer.CommitAsync(tx, token);
                if (receipt.IsReplay)
                    photos.Delete(key);
                return new(row.Detail(actor), receipt.IsReplay);
            }
            catch
            {
                if (!commitStarted)
                {
                    try
                    {
                        await tx.RollbackAsync(CancellationToken.None);
                        rolledBack = true;
                    }
                    catch (Exception rollbackError) when (rollbackError is SqlException or InvalidOperationException)
                    {
                        // Failure to confirm rollback must leave the file for maintenance.
                    }
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            // A failed commit acknowledgement can conceal a successful commit. Never delete its file.
            if (commitStarted)
            {
                try
                {
                    await using var check = connections.Create();
                    var id = await check.QuerySingleOrDefaultAsync<long?>(Command("SELECT report_id FROM dbo.mobile_report WHERE submitted_by=@UserId AND request_id=@RequestId AND payload_hash=@Hash", new
                    {
                        UserId = actor.UserId,
                        RequestId = metadata.RequestId,
                        Hash = hash
                    }, null, CancellationToken.None));
                    if (id is not null)
                    {
                        var recovered = await ReadAsync(check, null, actor, id.Value, CancellationToken.None);
                        if (recovered is not null)
                            return new(recovered.Detail(actor), true);
                    }
                }
                catch (Exception verificationError) when (verificationError is SqlException or InvalidOperationException) {/* Unknown outcome: preserve file for by-request recovery/age-based maintenance. */}
            }
            if (!commitStarted && (rolledBack || c.State != ConnectionState.Open))
                photos.Delete(key);
            if (ex is SqlException sql)
                throw Translate(sql);
            throw;
        }
    }
    public async Task<ReportSubmission> SubmitEtaAsync(ReportActor actor, EtaMetadata metadata, CancellationToken token)
    {
        if (!actor.CanSubmit)
            throw Forbidden();
        if (metadata.RequestId == Guid.Empty || !long.TryParse(metadata.AppointmentId, NumberStyles.None, CultureInfo.InvariantCulture, out var appointmentId) || appointmentId <= 0 || metadata.ProposedEta == default || string.IsNullOrWhiteSpace(metadata.Description) || metadata.Description.Trim().Length > 500)
            throw new MobileReportException(400, "INVALID_ETA", "请检查预约、预计到达时间和说明。");
        metadata = metadata with
        {
            AppointmentId = appointmentId.ToString(CultureInfo.InvariantCulture),
            ProposedEta = metadata.ProposedEta.ToUniversalTime(),
            Description = metadata.Description.Trim()
        };
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { reportType = "ETA", metadata }));
        var args = new DynamicParameters(new
        {
            user_id = actor.UserId,
            request_id = metadata.RequestId,
            appointment_id = appointmentId,
            description = metadata.Description,
            payload_hash = hash
        });
        // datetime2 preserves the same seven fractional digits used by the canonical payload.
        args.Add("proposed_eta", metadata.ProposedEta.UtcDateTime, DbType.DateTime2);
        await using var c = connections.Create();
        await c.OpenAsync(token);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(token);
        try
        {
            // SQL checks replay before the moving time/visit eligibility window.
            var receipt = await c.QuerySingleAsync<Receipt>(Command("dbo.usp_mobile_submit_eta", args, tx, token, CommandType.StoredProcedure));
            var row = await ReadAsync(c, tx, actor, receipt.ReportId, token) ?? throw new InvalidOperationException("Receipt missing.");
            await tx.CommitAsync(token);
            return new(row.Detail(actor), receipt.IsReplay);
        }
        catch (SqlException ex) { throw Translate(ex); }
    }
    public async Task<ReportDetail?> GetAsync(ReportActor actor, long reportId, CancellationToken token)
    {
        await using var c = connections.Create();
        return (await ReadAsync(c, null, actor, reportId, token))?.Detail(actor);
    }
    public async Task<ReportDetail?> FindByRequestAsync(ReportActor actor, Guid requestId, CancellationToken token)
    {
        await using var c = connections.Create();
        var r = await c.QuerySingleOrDefaultAsync<Row>(Command(SelectSql + " AND r.submitted_by=@UserId AND r.request_id=@RequestId", new
        {
            All = false,
            UserId = actor.UserId,
            RequestId = requestId
        }, null, token));
        return r?.Detail(actor);
    }
    public async Task<ReportPage> ListAsync(ReportActor actor, string scope, int page, int pageSize, long? appointmentId, string? status, CancellationToken token)
    {
        if (scope == "all" && !actor.CanProcess)
            throw Forbidden();
        if (scope is not ("all" or "mine") || page < 1 || pageSize is < 1 or > 100 || appointmentId <= 0 || status is not (null or "SUBMITTED" or "ACCEPTED" or "REJECTED"))
            throw Invalid();
        var args = new
        {
            All = scope == "all" && actor.CanProcess,
            UserId = actor.UserId,
            AppointmentId = appointmentId,
            Status = status,
            Offset = ((long)page - 1) * pageSize,
            PageSize = pageSize
        };
        await using var c = connections.Create();
        var filter = " AND (@AppointmentId IS NULL OR a.appointment_id=@AppointmentId) AND (@Status IS NULL OR r.status_code=@Status)";
        var rows = (await c.QueryAsync<Row>(Command(SelectSql + filter + " ORDER BY r.submitted_at DESC,r.report_id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", args, null, token))).ToArray();
        var count = await c.ExecuteScalarAsync<int>(Command("SELECT COUNT(*) FROM dbo.mobile_report r JOIN dbo.arrival_appointment a ON a.port_call_id=r.port_call_id WHERE (@All=1 OR r.submitted_by=@UserId)" + filter, args, null, token));
        return new(rows.Select(r => r.Summary()).ToArray(), count, page, pageSize);
    }
    public async Task<ProtectedPhoto?> PhotoAsync(ReportActor actor, long photoId, CancellationToken token)
    {
        await using var c = connections.Create();
        var key = await c.QuerySingleOrDefaultAsync<string>(Command("SELECT p.storage_key FROM dbo.report_photo p JOIN dbo.mobile_report r ON r.report_id=p.report_id WHERE p.photo_id=@PhotoId AND (@All=1 OR r.submitted_by=@UserId)", new
        {
            PhotoId = photoId,
            All = actor.CanProcess,
            UserId = actor.UserId
        }, null, token));
        return key is null ? null : new(photos.PathFor(key));
    }
    public async Task<ReportDetail> DecideAsync(ReportActor actor, long reportId, string decision, string comment, string expectedVersion, CancellationToken token)
    {
        if (!actor.CanProcess)
            throw Forbidden();
        byte[] version;
        try
        {
            version = Convert.FromBase64String(expectedVersion);
        }
        catch (FormatException) { throw Invalid(); }
        if (version.Length != 8 || decision is not ("ACCEPTED" or "REJECTED") || string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 500)
            throw Invalid();
        await using var c = connections.Create();
        await c.OpenAsync(token);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(token);
        try
        {
            await c.ExecuteAsync(Command("dbo.usp_mobile_decide", new
            {
                user_id = actor.UserId,
                report_id = reportId,
                decision,
                comment = comment.Trim(),
                expected_version = version
            }, tx, token, CommandType.StoredProcedure));
            var row = await ReadAsync(c, tx, actor, reportId, token) ?? throw new InvalidOperationException("Receipt missing.");
            await tx.CommitAsync(token);
            return row.Detail(actor);
        }
        catch (SqlException ex) { throw Translate(ex); }
    }
    private static Exception Translate(SqlException ex) => ex.Number switch { 52503 => Forbidden(), 52504 => new MobileReportException(400, "VISIT_NOT_ELIGIBLE", "该航次当前不可报送。"), 52505 => new MobileReportException(404, "REPORT_NOT_FOUND", "报送不存在。"), 52506 => new MobileReportException(400, "INVALID_ETA", "预计到达时间须晚于当前时间且在未来30天内，请检查报送内容。"), 52507 => new MobileReportException(409, "ETA_EXPIRED", "预计到达时间已过期，可退回并请报送人更新。"), 52509 => new MobileReportException(409, "REPORT_CONFLICT", "请求内容已变化或记录已处理，请核对原记录。"), _ => ex };
    private sealed class Receipt
    {
        public long ReportId
        {
            get; init;
        }
        public bool IsReplay
        {
            get; init;
        }
    }
    private sealed class Row
    {
        public string ReportId { get; init; } = "";
        public string RequestId { get; init; } = "";
        public string AppointmentId { get; init; } = "";
        public string AppointmentNo { get; init; } = "";
        public string CallNo { get; init; } = "";
        public string ShipName { get; init; } = "";
        public string ReportType { get; init; } = "";
        public string? Category
        {
            get; init;
        }
        public string Description { get; init; } = "";
        public DateTimeOffset? ProposedEta { get; init; }
        public string StatusCode { get; init; } = "";
        public DateTimeOffset SubmittedAt
        {
            get; init;
        }
        public string SubmittedByUserId { get; init; } = "";
        public string SubmittedByName { get; init; } = "";
        public DateTimeOffset? ProcessedAt
        {
            get; init;
        }
        public string? ProcessedByName
        {
            get; init;
        }
        public string? ProcessComment
        {
            get; init;
        }
        public byte[] Version { get; init; } = [];
        public string? PhotoId
        {
            get; init;
        }
        public string MimeType { get; init; } = "";
        public long ByteSize
        {
            get; init;
        }
        public DateTimeOffset CapturedAt
        {
            get; init;
        }
        public decimal? Latitude
        {
            get; init;
        }
        public decimal? Longitude
        {
            get; init;
        }
        public double? AccuracyMeters
        {
            get; init;
        }
        public DateTimeOffset? LocationAt
        {
            get; init;
        }
        public string? CoordinateSystem
        {
            get; init;
        }
        public string? StorageKey
        {
            get; init;
        }
        public ReportSummary Summary() => new(ReportId, RequestId, AppointmentId, AppointmentNo, CallNo, ShipName, ReportType, Category, Description, StatusCode, SubmittedAt, SubmittedByUserId, SubmittedByName, ProposedEta);
        public ReportDetail Detail(ReportActor a) => new(ReportId, RequestId, AppointmentId, AppointmentNo, CallNo, ShipName, ReportType, Category, Description, StatusCode, SubmittedAt, SubmittedByUserId, SubmittedByName, ProcessedAt, ProcessedByName, ProcessComment, Convert.ToBase64String(Version), a.CanProcess && StatusCode == "SUBMITTED", PhotoId is null ? null : new(PhotoId, MimeType, ByteSize, CapturedAt, Latitude is null ? null : new(Latitude.Value, Longitude!.Value, AccuracyMeters!.Value, LocationAt!.Value, CoordinateSystem!)), ProposedEta);
    }
}
