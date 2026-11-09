/* 精确读取一项预约，供详情独立选择使用；不改变列表筛选，不部署新数据库对象。 */
SELECT CONVERT(varchar(20), aa.appointment_id) AS AppointmentId,
    aa.appointment_no AS AppointmentNo, s.ship_name AS ShipName, s.imo_no AS ImoNo, s.mmsi AS Mmsi,
    TODATETIMEOFFSET(aa.requested_arrival_at, '+00:00') AS RequestedArrivalAt,
    TODATETIMEOFFSET(aa.requested_departure_at, '+00:00') AS RequestedDepartureAt,
    aa.appointment_status_code AS AppointmentStatusCode, reviewer.display_name AS ReviewerName,
    TODATETIMEOFFSET(aa.reviewed_at, '+00:00') AS ReviewedAt, aa.review_comment AS ReviewComment
FROM dbo.arrival_appointment AS aa
INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
INNER JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
LEFT JOIN dbo.user_account AS reviewer ON reviewer.user_id = aa.reviewed_by
WHERE aa.appointment_id = @AppointmentId;
