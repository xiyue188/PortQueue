/* 停留主键独立定位，事件时间线追溯同一次到访，保留每个事件所属停留主键。 */
SELECT (
    SELECT CONVERT(varchar(20),q.queue_entry_id) AS QueueEntryId,
        CONVERT(varchar(20),q.anchorage_id) AS AnchorageId,a.anchorage_name AS AnchorageName,
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
        plan_row.berth_name AS BerthName,CONVERT(varchar(20),plan_row.revision_id) AS ConfirmedRevisionId,
        TODATETIMEOFFSET(aa.requested_arrival_at,'+00:00') AS RequestedArrivalAt,
        TODATETIMEOFFSET(aa.submitted_at,'+00:00') AS SubmittedAt,
        TODATETIMEOFFSET(plan_row.confirmed_at,'+00:00') AS PlanConfirmedAt,
        TODATETIMEOFFSET(aa.reviewed_at,'+00:00') AS ReviewedAt,
        reviewer.display_name AS ReviewerName,aa.appointment_status_code AS AppointmentStatusCode,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),e.event_id) AS EventId,e.event_type_code AS EventTypeCode,
                TODATETIMEOFFSET(e.event_time,'+00:00') AS EventTime,
                CONVERT(varchar(20),e.queue_entry_id) AS QueueEntryId,recorder.display_name AS RecordedByName
            FROM dbo.v_port_event e LEFT JOIN dbo.user_account recorder ON recorder.user_id=e.recorded_by
            WHERE e.appointment_id=q.appointment_id ORDER BY e.event_time,e.event_id
            FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Events
    FROM dbo.v_anchorage_queue q JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id
    JOIN dbo.anchorage_queue_entry raw ON raw.queue_entry_id=q.queue_entry_id
    JOIN dbo.port p ON p.port_id=a.port_id JOIN dbo.port_call pc ON pc.port_call_id=q.port_call_id
    JOIN dbo.arrival_appointment aa ON aa.appointment_id=q.appointment_id
    LEFT JOIN dbo.user_account reviewer ON reviewer.user_id=aa.reviewed_by
    -- 当前停留只用有效当前计划；历史停留保留离锚前最后确认版本，不被后来的安排覆盖。
    OUTER APPLY (SELECT TOP(1) r.planned_berthing_at,r.planned_unberthing_at,r.confirmed_at,r.revision_id,b.berth_name
        FROM dbo.berth_plan bp JOIN dbo.berth_plan_revision r ON r.berth_plan_id=bp.berth_plan_id
        JOIN dbo.berth b ON b.berth_id=r.berth_id JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
        WHERE bp.appointment_id=q.appointment_id AND r.confirmed_at IS NOT NULL AND t.port_id=a.port_id
          AND ((q.left_anchorage_at IS NULL AND r.is_current=1
                AND bp.plan_status_code IN ('CONFIRMED','EXECUTING') AND aa.appointment_status_code='APPROVED')
            OR (q.left_anchorage_at IS NOT NULL AND r.confirmed_at<=q.left_anchorage_at))
        ORDER BY r.confirmed_at DESC,r.version_no DESC) plan_row
    WHERE q.queue_entry_id=@QueueEntryId AND p.port_code=@HomePortCode
    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER
) AS Payload;
