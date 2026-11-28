/* 无目的港外键的到访按单港模型处理；已有计划必须存在本港版本。CHARINDEX 按关键词字面匹配。 */
WITH visits AS (
    SELECT aa.appointment_id,aa.appointment_no,pc.port_call_id,pc.call_no,s.ship_id,s.ship_name,
        aa.appointment_status_code,aa.requested_arrival_at,aa.estimated_operation_hours,s.ship_type_item_id,
        d.item_name AS ship_type_name,s.length_m,aa.declared_draft_m,s.deadweight_t,bp.plan_status_code,bp.berth_plan_id,
        stay.queue_entry_id,stay.anchorage_name,stay.manual_estimated_berthing_at
    FROM dbo.arrival_appointment aa JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
    JOIN dbo.ship s ON s.ship_id=pc.ship_id JOIN dbo.dictionary_item d ON d.dictionary_item_id=s.ship_type_item_id
    LEFT JOIN dbo.berth_plan bp ON bp.appointment_id=aa.appointment_id
    OUTER APPLY (SELECT TOP(1) q.queue_entry_id,q.anchorage_name,raw.manual_estimated_berthing_at
        FROM dbo.v_anchorage_queue q JOIN dbo.anchorage_queue_entry raw ON raw.queue_entry_id=q.queue_entry_id
        JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id JOIN dbo.port p ON p.port_id=a.port_id
        WHERE q.appointment_id=aa.appointment_id AND p.port_code=@HomePortCode
        ORDER BY CASE WHEN q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL THEN 0 ELSE 1 END,
            q.entered_anchorage_at DESC,q.queue_entry_id DESC) stay
    WHERE EXISTS(SELECT 1 FROM dbo.port WHERE port_code=@HomePortCode)
      AND ((@Scope='waiting' AND aa.appointment_status_code='APPROVED' AND (bp.berth_plan_id IS NULL OR bp.plan_status_code='DRAFT'))
        OR (@Scope='all' AND (aa.appointment_status_code='APPROVED' OR bp.berth_plan_id IS NOT NULL)))
      AND (bp.berth_plan_id IS NULL OR EXISTS(SELECT 1 FROM dbo.berth_plan_revision r
          JOIN dbo.berth b ON b.berth_id=r.berth_id JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
          JOIN dbo.port p ON p.port_id=t.port_id WHERE r.berth_plan_id=bp.berth_plan_id AND p.port_code=@HomePortCode))
      AND (@Keyword=N'' OR CHARINDEX(@Keyword,s.ship_name)>0 OR CHARINDEX(@Keyword,aa.appointment_no)>0 OR CHARINDEX(@Keyword,pc.call_no)>0)
)
SELECT (
    SELECT @Page AS Page,@PageSize AS PageSize,(SELECT COUNT_BIG(*) FROM visits) AS TotalCount,
        JSON_QUERY(COALESCE((SELECT CONVERT(varchar(20),appointment_id) AS AppointmentId,appointment_no AS AppointmentNo,
            CONVERT(varchar(20),port_call_id) AS PortCallId,call_no AS CallNo,CONVERT(varchar(20),ship_id) AS ShipId,ship_name AS ShipName,
            appointment_status_code AS AppointmentStatusCode,TODATETIMEOFFSET(requested_arrival_at,'+00:00') AS RequestedArrivalAt,
            estimated_operation_hours AS EstimatedOperationHours,CONVERT(varchar(20),ship_type_item_id) AS ShipTypeItemId,
            ship_type_name AS ShipTypeName,length_m AS LengthM,declared_draft_m AS DeclaredDraftM,deadweight_t AS DeadweightT,
            plan_status_code AS PlanStatusCode,CONVERT(varchar(20),berth_plan_id) AS BerthPlanId,
            CONVERT(varchar(20),queue_entry_id) AS QueueEntryId,anchorage_name AS AnchorageName,
            TODATETIMEOFFSET(manual_estimated_berthing_at,'+00:00') AS ManualEstimatedBerthingAt
            FROM visits ORDER BY requested_arrival_at,appointment_id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) AS Items
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER
) AS Payload;
