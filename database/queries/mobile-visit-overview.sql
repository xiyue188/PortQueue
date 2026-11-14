/* Read-only single-statement snapshot (requires M2 schema). Appointment is the sole visit identity;
   actual anchorage times are reconstructed by the existing event-backed view. */
SELECT CONVERT(varchar(20),aa.appointment_id) AS AppointmentId,
 aa.appointment_no AS AppointmentNo,s.ship_name AS ShipName,pc.call_no AS CallNo,
 aa.appointment_status_code AS AppointmentStatusCode,pc.call_status_code AS CallStatusCode,
 TODATETIMEOFFSET(pc.planned_arrival_at,'+00:00') AS PlannedArrivalAt,
 q.anchorage_name AS AnchorageName,q.queue_position AS QueuePosition,q.queue_status_code AS QueueStatusCode,
 TODATETIMEOFFSET(q.entered_anchorage_at,'+00:00') AS EnteredAt,
 TODATETIMEOFFSET(q.left_anchorage_at,'+00:00') AS LeftAt,
 p.berth_name AS BerthName,p.version_no AS VersionNo,p.plan_status_code AS PlanStatusCode,
 TODATETIMEOFFSET(p.planned_berthing_at,'+00:00') AS PlannedBerthingAt,
 TODATETIMEOFFSET(p.planned_unberthing_at,'+00:00') AS PlannedUnberthingAt,
 CONVERT(varchar(20),eta.report_id) AS EtaReportId,
 TODATETIMEOFFSET(eta.proposed_eta,'+00:00') AS ProposedEta,
 TODATETIMEOFFSET(eta.processed_at,'+00:00') AS EtaProcessedAt,
 TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS ServerTime
FROM dbo.arrival_appointment aa
JOIN dbo.port_call pc ON pc.port_call_id=aa.port_call_id
JOIN dbo.ship s ON s.ship_id=pc.ship_id
OUTER APPLY (
 SELECT TOP(1) v.* FROM dbo.v_anchorage_queue v WHERE v.appointment_id=aa.appointment_id
 ORDER BY CASE WHEN v.queue_status_code IN ('WAITING','ASSIGNED') THEN 0 ELSE 1 END,
 v.created_at DESC,v.queue_entry_id DESC
) q
OUTER APPLY (
 SELECT TOP(1) v.* FROM dbo.v_current_berth_plan v WHERE v.appointment_id=aa.appointment_id
 AND v.plan_status_code IN ('CONFIRMED','EXECUTING') AND v.confirmed_at IS NOT NULL AND v.confirmed_by IS NOT NULL
 ORDER BY v.berth_plan_id DESC
) p
OUTER APPLY (
 SELECT TOP(1) r.report_id,r.proposed_eta,r.processed_at FROM dbo.mobile_report r
 WHERE r.port_call_id=pc.port_call_id AND r.report_type='ETA' AND r.status_code='ACCEPTED'
 ORDER BY r.processed_at DESC,r.report_id DESC
) eta
WHERE aa.appointment_id=@AppointmentId;
