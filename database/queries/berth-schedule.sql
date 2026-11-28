/* 单条语句共享快照：候选只表示待确认意图；实际占用始终绑定靠泊事件的执行版本。 */
WITH home_resources AS (
    SELECT b.berth_id,b.berth_code,b.berth_name,b.length_m,b.max_draft_m,b.max_dwt,b.status_code,
        t.terminal_name,t.status_code AS terminal_status_code,p.status_code AS port_status_code
    FROM dbo.berth b JOIN dbo.terminal t ON t.terminal_id=b.terminal_id JOIN dbo.port p ON p.port_id=t.port_id
    WHERE p.port_code=@HomePortCode
), revisions AS (
    SELECT r.*,bp.appointment_id,bp.plan_status_code,s.ship_name,pc.call_no,
        editor.display_name AS revised_by_name,confirmer.display_name AS confirmed_by_name
    FROM dbo.berth_plan_revision r JOIN dbo.berth_plan bp ON bp.berth_plan_id=r.berth_plan_id
    JOIN dbo.arrival_appointment aa ON aa.appointment_id=bp.appointment_id
    JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id JOIN dbo.ship s ON s.ship_id=pc.ship_id
    JOIN home_resources b ON b.berth_id=r.berth_id
    JOIN dbo.user_account editor ON editor.user_id=r.revised_by
    LEFT JOIN dbo.user_account confirmer ON confirmer.user_id=r.confirmed_by
    WHERE ((aa.appointment_status_code='APPROVED' AND bp.plan_status_code IN ('DRAFT','CONFIRMED','EXECUTING'))
        OR (aa.appointment_status_code='COMPLETED' AND bp.plan_status_code='COMPLETED'))
      AND r.planned_berthing_at<@ToUtc AND r.planned_unberthing_at>@FromUtc
      AND ((r.is_current=1 AND (bp.plan_status_code='DRAFT' OR r.confirmed_at IS NOT NULL))
        OR (bp.plan_status_code IN ('DRAFT','CONFIRMED','EXECUTING') AND r.confirmed_at IS NULL
            AND r.version_no>(SELECT current_r.version_no FROM dbo.berth_plan_revision current_r
                WHERE current_r.berth_plan_id=r.berth_plan_id AND current_r.is_current=1)
            AND r.version_no=(SELECT MAX(latest.version_no) FROM dbo.berth_plan_revision latest
                WHERE latest.berth_plan_id=r.berth_plan_id)))
)
SELECT (
    SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS ObservedAt,
        TODATETIMEOFFSET(@FromUtc,'+00:00') AS [From],TODATETIMEOFFSET(@ToUtc,'+00:00') AS [To],
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),b.berth_id) AS BerthId,b.berth_code AS Code,b.berth_name AS Name,
            b.terminal_name AS TerminalName,b.status_code AS StatusCode,b.terminal_status_code AS TerminalStatusCode,
            b.port_status_code AS PortStatusCode,b.length_m AS LengthM,b.max_draft_m AS MaxDraftM,b.max_dwt AS MaxDwt,
            JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),d.dictionary_item_id) AS ItemId,d.item_name AS Name
                FROM dbo.berth_ship_type bt JOIN dbo.dictionary_item d ON d.dictionary_item_id=bt.ship_type_item_id
                WHERE bt.berth_id=b.berth_id AND bt.relation_status='ENABLED' AND d.dictionary_type='SHIP_TYPE' AND d.is_enabled=1
                ORDER BY d.dictionary_item_id FOR JSON PATH),N'[]')) AS ShipTypes
            FROM home_resources b ORDER BY b.terminal_name,b.berth_code,b.berth_id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Resources,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),r.revision_id) AS RevisionId,CONVERT(varchar(20),r.berth_plan_id) AS BerthPlanId,
            CONVERT(varchar(20),r.appointment_id) AS AppointmentId,r.ship_name AS ShipName,r.call_no AS CallNo,
            CONVERT(varchar(20),r.berth_id) AS BerthId,r.version_no AS VersionNo,
            TODATETIMEOFFSET(r.planned_berthing_at,'+00:00') AS PlannedBerthingAt,
            TODATETIMEOFFSET(r.planned_unberthing_at,'+00:00') AS PlannedUnberthingAt,
            r.plan_status_code AS PlanStatusCode,r.is_current AS IsCurrent,
            TODATETIMEOFFSET(r.confirmed_at,'+00:00') AS ConfirmedAt,TODATETIMEOFFSET(r.revised_at,'+00:00') AS RevisedAt,
            r.revision_reason AS RevisionReason,r.revised_by_name AS RevisedByName,r.confirmed_by_name AS ConfirmedByName
            FROM revisions r ORDER BY r.planned_berthing_at,r.berth_plan_id,r.version_no
            FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Revisions,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),enter_event.appointment_id) AS AppointmentId,s.ship_name AS ShipName,pc.call_no AS CallNo,
            CONVERT(varchar(20),r.berth_id) AS BerthId,CONVERT(varchar(20),r.revision_id) AS ExecutedRevisionId,
            TODATETIMEOFFSET(enter_event.event_time,'+00:00') AS ActualBerthingAt,
            TODATETIMEOFFSET(leave_event.event_time,'+00:00') AS ActualUnberthingAt
            FROM dbo.v_port_event enter_event JOIN dbo.berth_plan_revision r ON r.revision_id=enter_event.executed_revision_id
            JOIN home_resources b ON b.berth_id=r.berth_id
            JOIN dbo.arrival_appointment aa ON aa.appointment_id=enter_event.appointment_id
            JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id JOIN dbo.ship s ON s.ship_id=pc.ship_id
            OUTER APPLY (SELECT TOP(1) e.event_time FROM dbo.v_port_event e
                WHERE e.appointment_id=enter_event.appointment_id AND e.event_type_code='UNBERTHING'
                    AND e.executed_revision_id=enter_event.executed_revision_id ORDER BY e.event_time,e.event_id) leave_event
            WHERE enter_event.event_type_code='BERTHING'
              AND enter_event.event_time<@ToUtc
              AND (leave_event.event_time IS NULL OR leave_event.event_time>@FromUtc)
            ORDER BY enter_event.event_time,enter_event.appointment_id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Occupancies
    FROM dbo.port p WHERE p.port_code=@HomePortCode
    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER
) AS Payload;
