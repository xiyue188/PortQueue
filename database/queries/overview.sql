/* 全港工作量概况：不跟随预约页筛选；单条 SELECT 在既有 RCSI 下读取一致快照。 */
SELECT
    (SELECT COUNT_BIG(*) FROM dbo.arrival_appointment WHERE appointment_status_code='PENDING') AS PendingAppointments,
    (SELECT COUNT_BIG(*) FROM dbo.v_anchorage_queue WHERE entered_anchorage_at IS NOT NULL AND left_anchorage_at IS NULL) AS AnchorageVessels,
    (SELECT COUNT_BIG(*) FROM dbo.v_current_berth_plan WHERE plan_status_code='CONFIRMED') AS ConfirmedPlans,
    (SELECT COUNT_BIG(*) FROM dbo.v_port_event) AS TotalEvents,
    (SELECT CONVERT(varchar(20),e.event_id) AS EventId, e.event_type_code AS EventTypeCode,
    TODATETIMEOFFSET(e.event_time,'+00:00') AS EventTime, s.ship_name AS ShipName,
    CONVERT(varchar(20), aa.appointment_id) AS AppointmentId, aa.appointment_no AS AppointmentNo
FROM dbo.v_port_event e
JOIN dbo.arrival_appointment aa ON aa.appointment_id=e.appointment_id
JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
JOIN dbo.ship s ON s.ship_id=pc.ship_id
ORDER BY e.event_time DESC,e.event_id DESC
OFFSET @EventOffset ROWS FETCH NEXT @EventPageSize ROWS ONLY
FOR JSON PATH) AS RecentEventsJson;
