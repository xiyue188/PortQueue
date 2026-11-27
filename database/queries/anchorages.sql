/* 单语句读取：设施、实际占用、有效队列及定位使用同一个 RCSI 读取快照。
   appointmentId 只定位当前/最近停留，不筛选全港设施。 */
SELECT (
    SELECT CONVERT(varchar(20),p.port_id) AS PortId,p.port_code AS PortCode,p.port_name AS PortName,
        TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS ObservedAt,
        JSON_QUERY((SELECT TOP(1) CONVERT(varchar(20),q.queue_entry_id) AS QueueEntryId,
                CONVERT(varchar(20),q.anchorage_id) AS AnchorageId
            FROM dbo.v_anchorage_queue q JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id
            WHERE q.appointment_id=@AppointmentId AND a.port_id=p.port_id
            ORDER BY CASE WHEN q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL THEN 0 ELSE 1 END,
                q.entered_anchorage_at DESC,q.queue_entry_id DESC
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)) AS LocatedStay,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),a.anchorage_id) AS AnchorageId,
                a.anchorage_code AS Code,a.anchorage_name AS Name,a.status_code AS StatusCode,
                a.capacity_vessel_count AS Capacity,
                (SELECT COUNT_BIG(*) FROM dbo.v_anchorage_queue q WHERE q.anchorage_id=a.anchorage_id
                    AND q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL) AS OccupiedCount,
                (SELECT COUNT_BIG(*) FROM dbo.v_anchorage_queue q WHERE q.anchorage_id=a.anchorage_id
                    AND q.queue_status_code IN ('WAITING','ASSIGNED')) AS QueueCount
            FROM dbo.anchorage a WHERE a.port_id=p.port_id
            ORDER BY a.anchorage_code,a.anchorage_id
            FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Items
    FROM dbo.port p WHERE p.port_code=@HomePortCode
    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER
) AS Payload;
