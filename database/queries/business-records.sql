/* 已有三视图的分页入口；种类由白名单控制，始终静态SQL，不拼表名。 */
WITH records AS (
 SELECT CONVERT(varchar(20),queue_entry_id) Id,CONVERT(varchar(20),appointment_id) AppointmentId,appointment_no AppointmentNo,
   ship_name ShipName,anchorage_name Location,queue_status_code Status,entered_anchorage_at StartedAt,left_anchorage_at EndedAt,queue_position Position,CAST(NULL AS int) Version
 FROM dbo.v_anchorage_queue WHERE @Kind='queue'
 UNION ALL
 SELECT CONVERT(varchar(20),berth_plan_id),CONVERT(varchar(20),appointment_id),appointment_no,ship_name,berth_name,plan_status_code,
   planned_berthing_at,planned_unberthing_at,NULL,version_no FROM dbo.v_current_berth_plan WHERE @Kind='plans'
 UNION ALL
 SELECT CONVERT(varchar(20),e.event_id),CONVERT(varchar(20),e.appointment_id),a.appointment_no,s.ship_name,u.display_name,e.event_type_code,
   e.event_time,NULL,NULL,NULL FROM dbo.v_port_event e
 JOIN dbo.arrival_appointment a ON a.appointment_id=e.appointment_id JOIN dbo.port_call pc ON pc.port_call_id=a.port_call_id
 JOIN dbo.ship s ON s.ship_id=pc.ship_id JOIN dbo.user_account u ON u.user_id=e.recorded_by WHERE @Kind='events'
), filtered AS (SELECT * FROM records WHERE @Keyword=N'' OR CHARINDEX(@Keyword,ShipName)>0 OR CHARINDEX(@Keyword,AppointmentNo)>0 OR CHARINDEX(@Keyword,Location)>0)
SELECT (SELECT COUNT_BIG(*) FROM filtered) TotalCount,
 COALESCE((SELECT Id,AppointmentId,AppointmentNo,ShipName,Location,Status,TODATETIMEOFFSET(StartedAt,'+00:00') StartedAt,
    TODATETIMEOFFSET(EndedAt,'+00:00') EndedAt,Position,Version FROM filtered
  ORDER BY CASE WHEN @Kind='queue' AND Status IN('WAITING','ASSIGNED') THEN 0 ELSE 1 END,CASE WHEN @Kind='queue' THEN Location END,
    CASE WHEN @Kind='queue' THEN Position END,StartedAt DESC,CONVERT(bigint,Id) DESC
  OFFSET @Offset ROWS FETCH NEXT 20 ROWS ONLY FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]') ItemsJson;
