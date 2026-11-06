namespace PortQueue.Application.Appointments;

/// <summary>预约列表的一页数据。主键以字符串传输，避免浏览器丢失 bigint 精度。</summary>
public sealed record AppointmentListItem(
    string AppointmentId,
    string AppointmentNo,
    string ShipName,
    string? ImoNo,
    string? Mmsi,
    DateTimeOffset RequestedArrivalAt,
    DateTimeOffset RequestedDepartureAt,
    string AppointmentStatusCode,
    string? ReviewerName,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment);

/// <summary>总数属于完整筛选结果；请求空页时也返回实际总数。</summary>
public sealed record AppointmentPage(
    IReadOnlyList<AppointmentListItem> Items, int Page, int PageSize, long TotalCount);
