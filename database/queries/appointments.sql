/*
   预约列表只读查询，由 Infrastructure 嵌入并参数化执行，不单独部署为数据库对象。
   @Status 可空；@Pattern 是已转义的字面包含匹配；@Offset/@PageSize 为已校验分页参数。
   单条语句在 READ_COMMITTED_SNAPSHOT 下读取同一提交快照；空页仍保留总数。
*/
WITH filtered AS
(
    SELECT aa.appointment_id, aa.appointment_no, aa.requested_arrival_at,
        aa.requested_departure_at, aa.appointment_status_code,
        aa.reviewed_by, aa.reviewed_at, aa.review_comment,
        s.ship_name, s.imo_no, s.mmsi
    FROM dbo.arrival_appointment AS aa
    INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
    INNER JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
    WHERE (@Status IS NULL OR aa.appointment_status_code = @Status)
      AND (@Pattern IS NULL
        OR aa.appointment_no LIKE @Pattern ESCAPE N'\'
        OR s.ship_name LIKE @Pattern ESCAPE N'\'
        OR s.imo_no LIKE @Pattern ESCAPE N'\'
        OR s.mmsi LIKE @Pattern ESCAPE N'\')
)
SELECT totals.TotalCount,
    CONVERT(varchar(20), page.appointment_id) AS AppointmentId,
    page.appointment_no AS AppointmentNo,
    page.ship_name AS ShipName, page.imo_no AS ImoNo, page.mmsi AS Mmsi,
    TODATETIMEOFFSET(page.requested_arrival_at, '+00:00') AS RequestedArrivalAt,
    TODATETIMEOFFSET(page.requested_departure_at, '+00:00') AS RequestedDepartureAt,
    page.appointment_status_code AS AppointmentStatusCode,
    reviewer.display_name AS ReviewerName,
    TODATETIMEOFFSET(page.reviewed_at, '+00:00') AS ReviewedAt,
    page.review_comment AS ReviewComment
FROM (SELECT COUNT_BIG(*) AS TotalCount FROM filtered) AS totals
OUTER APPLY
(
    SELECT * FROM filtered
    ORDER BY requested_arrival_at DESC, appointment_id DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
) AS page
LEFT JOIN dbo.user_account AS reviewer ON reviewer.user_id = page.reviewed_by
ORDER BY page.requested_arrival_at DESC, page.appointment_id DESC;
