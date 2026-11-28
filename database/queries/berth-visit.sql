/* 详情独立于清单筛选；全版本和实际事件共用一次查询快照，历史设施无需处于启用状态。 */
SELECT (
    SELECT CONVERT(varchar(20),aa.appointment_id) AS AppointmentId,aa.appointment_no AS AppointmentNo,
        CONVERT(varchar(20),pc.port_call_id) AS PortCallId,pc.call_no AS CallNo,CONVERT(varchar(20),s.ship_id) AS ShipId,s.ship_name AS ShipName,
        aa.appointment_status_code AS AppointmentStatusCode,TODATETIMEOFFSET(aa.requested_arrival_at,'+00:00') AS RequestedArrivalAt,
        aa.estimated_operation_hours AS EstimatedOperationHours,CONVERT(varchar(20),s.ship_type_item_id) AS ShipTypeItemId,
        d.item_name AS ShipTypeName,s.length_m AS LengthM,aa.declared_draft_m AS DeclaredDraftM,s.deadweight_t AS DeadweightT,
        bp.plan_status_code AS PlanStatusCode,CONVERT(varchar(20),bp.berth_plan_id) AS BerthPlanId,
        CONVERT(varchar(20),stay.queue_entry_id) AS QueueEntryId,stay.anchorage_name AS AnchorageName,
        TODATETIMEOFFSET(stay.manual_estimated_berthing_at,'+00:00') AS ManualEstimatedBerthingAt,
        TODATETIMEOFFSET(stay.entered_anchorage_at,'+00:00') AS EnteredAt,TODATETIMEOFFSET(stay.left_anchorage_at,'+00:00') AS LeftAt,
        TODATETIMEOFFSET(aa.submitted_at,'+00:00') AS SubmittedAt,TODATETIMEOFFSET(aa.reviewed_at,'+00:00') AS ReviewedAt,
        reviewer.display_name AS ReviewerName,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),r.revision_id) AS RevisionId,CONVERT(varchar(20),r.berth_plan_id) AS BerthPlanId,
            CONVERT(varchar(20),aa.appointment_id) AS AppointmentId,s.ship_name AS ShipName,pc.call_no AS CallNo,
            CONVERT(varchar(20),r.berth_id) AS BerthId,r.version_no AS VersionNo,
            TODATETIMEOFFSET(r.planned_berthing_at,'+00:00') AS PlannedBerthingAt,
            TODATETIMEOFFSET(r.planned_unberthing_at,'+00:00') AS PlannedUnberthingAt,
            bp.plan_status_code AS PlanStatusCode,r.is_current AS IsCurrent,TODATETIMEOFFSET(r.confirmed_at,'+00:00') AS ConfirmedAt,
            TODATETIMEOFFSET(r.revised_at,'+00:00') AS RevisedAt,r.revision_reason AS RevisionReason,
            editor.display_name AS RevisedByName,confirmer.display_name AS ConfirmedByName
            FROM dbo.berth_plan_revision r JOIN dbo.berth b ON b.berth_id=r.berth_id
            JOIN dbo.terminal t ON t.terminal_id=b.terminal_id JOIN dbo.port p ON p.port_id=t.port_id
            JOIN dbo.user_account editor ON editor.user_id=r.revised_by LEFT JOIN dbo.user_account confirmer ON confirmer.user_id=r.confirmed_by
            WHERE r.berth_plan_id=bp.berth_plan_id AND p.port_code=@HomePortCode
            ORDER BY r.version_no FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Versions,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),e.event_id) AS EventId,e.event_type_code AS EventTypeCode,
            TODATETIMEOFFSET(e.event_time,'+00:00') AS EventTime,CONVERT(varchar(20),e.queue_entry_id) AS QueueEntryId,
            CONVERT(varchar(20),e.executed_revision_id) AS ExecutedRevisionId,recorder.display_name AS RecordedByName
            FROM dbo.v_port_event e LEFT JOIN dbo.user_account recorder ON recorder.user_id=e.recorded_by
            WHERE e.appointment_id=aa.appointment_id
              AND (e.executed_revision_id IS NULL OR EXISTS(SELECT 1 FROM dbo.berth_plan_revision r
                  JOIN dbo.berth b ON b.berth_id=r.berth_id JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
                  JOIN dbo.port p ON p.port_id=t.port_id WHERE r.revision_id=e.executed_revision_id AND p.port_code=@HomePortCode))
              AND (e.queue_entry_id IS NULL OR EXISTS(SELECT 1 FROM dbo.v_anchorage_queue q
                  JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id JOIN dbo.port p ON p.port_id=a.port_id
                  WHERE q.queue_entry_id=e.queue_entry_id AND p.port_code=@HomePortCode))
            ORDER BY e.event_time,e.event_id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Events
    FROM dbo.arrival_appointment aa JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
    JOIN dbo.ship s ON s.ship_id=pc.ship_id JOIN dbo.dictionary_item d ON d.dictionary_item_id=s.ship_type_item_id
    LEFT JOIN dbo.user_account reviewer ON reviewer.user_id=aa.reviewed_by LEFT JOIN dbo.berth_plan bp ON bp.appointment_id=aa.appointment_id
    OUTER APPLY (SELECT TOP(1) q.queue_entry_id,q.anchorage_name,q.entered_anchorage_at,q.left_anchorage_at,raw.manual_estimated_berthing_at
        FROM dbo.v_anchorage_queue q JOIN dbo.anchorage_queue_entry raw ON raw.queue_entry_id=q.queue_entry_id
        JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id JOIN dbo.port p ON p.port_id=a.port_id
        WHERE q.appointment_id=aa.appointment_id AND p.port_code=@HomePortCode
        ORDER BY CASE WHEN q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL THEN 0 ELSE 1 END,
            q.entered_anchorage_at DESC,q.queue_entry_id DESC) stay
    WHERE aa.appointment_id=@AppointmentId AND EXISTS(SELECT 1 FROM dbo.port WHERE port_code=@HomePortCode)
      AND (aa.appointment_status_code='APPROVED' OR bp.berth_plan_id IS NOT NULL)
      AND (bp.berth_plan_id IS NULL OR EXISTS(SELECT 1 FROM dbo.berth_plan_revision r
          JOIN dbo.berth b ON b.berth_id=r.berth_id JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
          JOIN dbo.port p ON p.port_id=t.port_id WHERE r.berth_plan_id=bp.berth_plan_id AND p.port_code=@HomePortCode))
    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER
) AS Payload;
