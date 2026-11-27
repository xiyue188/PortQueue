/* 入锚候选来自批准预约。一次本港到访可多次入锚，但任一物理船只能有一段未关闭停留。 */
WITH candidates AS (
    SELECT aa.appointment_id,aa.appointment_no,pc.port_call_id,pc.call_no,s.ship_id,s.ship_name,
        aa.requested_arrival_at,aa.priority_level
    FROM dbo.arrival_appointment aa JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
    JOIN dbo.ship s ON s.ship_id=pc.ship_id
    WHERE aa.appointment_status_code='APPROVED' AND aa.reviewed_at IS NOT NULL AND s.status_code='ACTIVE'
      AND pc.call_status_code IN ('PLANNED','IN_PROGRESS')
      AND NOT EXISTS(SELECT 1 FROM dbo.v_anchorage_queue q WHERE q.ship_id=s.ship_id AND q.left_anchorage_at IS NULL)
      AND NOT EXISTS(SELECT 1 FROM dbo.v_port_event e WHERE e.appointment_id=aa.appointment_id AND e.event_type_code='PORT_ENTER')
      AND NOT EXISTS(SELECT 1 FROM dbo.v_port_event e JOIN dbo.arrival_appointment other_aa ON other_aa.appointment_id=e.appointment_id
          JOIN dbo.port_call other_pc ON other_pc.port_call_id=other_aa.port_call_id
          WHERE other_pc.ship_id=s.ship_id AND e.event_type_code='PORT_ENTER'
            AND NOT EXISTS(SELECT 1 FROM dbo.v_port_event finish WHERE finish.appointment_id=e.appointment_id AND finish.event_type_code='UNBERTHING'))
      AND (@Keyword=N'' OR CHARINDEX(@Keyword,s.ship_name)>0 OR CHARINDEX(@Keyword,aa.appointment_no)>0 OR CHARINDEX(@Keyword,pc.call_no)>0)
)
SELECT (SELECT COUNT_BIG(*) FROM candidates) AS TotalCount,
    COALESCE((SELECT CONVERT(varchar(20),appointment_id) AS AppointmentId,appointment_no AS AppointmentNo,
        CONVERT(varchar(20),port_call_id) AS PortCallId,call_no AS CallNo,CONVERT(varchar(20),ship_id) AS ShipId,ship_name AS ShipName,
        TODATETIMEOFFSET(requested_arrival_at,'+00:00') AS RequestedArrivalAt,priority_level AS PriorityLevel
        FROM candidates ORDER BY requested_arrival_at,appointment_id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY FOR JSON PATH),N'[]') AS ItemsJson;
