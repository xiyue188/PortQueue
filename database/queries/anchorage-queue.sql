/* 完整有效队列及撤销仍在场；无 TOP 截断。快照由同一份返回成员计算。 */
SELECT (
    SELECT CONVERT(varchar(20),a.anchorage_id) AS AnchorageId,
        TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS ObservedAt,
        a.capacity_vessel_count AS Capacity,a.status_code AS StatusCode,
        (SELECT COUNT(*) FROM dbo.v_anchorage_queue occupied WHERE occupied.anchorage_id=a.anchorage_id
            AND occupied.entered_anchorage_at IS NOT NULL AND occupied.left_anchorage_at IS NULL) AS OccupiedCount,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),q.queue_entry_id) AS QueueEntryId,
                CONVERT(varchar(20),q.anchorage_id) AS AnchorageId,
                CONVERT(varchar(20),q.appointment_id) AS AppointmentId,
                CONVERT(varchar(20),q.port_call_id) AS PortCallId,pc.call_no AS CallNo,
                q.appointment_no AS AppointmentNo,CONVERT(varchar(20),q.ship_id) AS ShipId,q.ship_name AS ShipName,
                q.queue_position AS QueuePosition,q.priority_level AS PriorityLevel,q.queue_status_code AS StatusCode,
                TODATETIMEOFFSET(q.entered_anchorage_at,'+00:00') AS EnteredAt,
                TODATETIMEOFFSET(q.left_anchorage_at,'+00:00') AS LeftAt,
                TODATETIMEOFFSET(q.estimated_berthing_at,'+00:00') AS LegacyEstimatedBerthingAt,
                TODATETIMEOFFSET(raw.manual_estimated_berthing_at,'+00:00') AS ManualEstimatedBerthingAt,
                CONVERT(varchar(20),raw.estimate_version) AS EstimateVersion,
                TODATETIMEOFFSET(plan_row.planned_berthing_at,'+00:00') AS ConfirmedBerthingAt,
                TODATETIMEOFFSET(plan_row.planned_unberthing_at,'+00:00') AS ConfirmedUnberthingAt,
                plan_row.berth_name AS BerthName,CONVERT(varchar(20),plan_row.revision_id) AS ConfirmedRevisionId
            FROM dbo.v_anchorage_queue q
            JOIN dbo.anchorage_queue_entry raw ON raw.queue_entry_id=q.queue_entry_id
            JOIN dbo.port_call pc ON pc.port_call_id=q.port_call_id
            JOIN dbo.arrival_appointment aa ON aa.appointment_id=q.appointment_id
            OUTER APPLY (SELECT bp.* FROM dbo.v_current_berth_plan bp
                JOIN dbo.berth b ON b.berth_id=bp.berth_id JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
                WHERE bp.appointment_id=q.appointment_id AND bp.confirmed_at IS NOT NULL
                  AND bp.plan_status_code IN ('CONFIRMED','EXECUTING') AND aa.appointment_status_code='APPROVED'
                  AND t.port_id=a.port_id) plan_row
            WHERE q.anchorage_id=a.anchorage_id AND (q.queue_status_code IN ('WAITING','ASSIGNED')
                OR (q.queue_status_code='CANCELLED' AND q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL))
            ORDER BY q.queue_position,q.queue_entry_id
            FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Items
    FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id
    WHERE a.anchorage_id=@AnchorageId AND p.port_code=@HomePortCode
    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER
) AS Payload;
