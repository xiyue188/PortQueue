/* 精确读取一项预约，供详情独立选择使用；不改变列表筛选，不部署新数据库对象。 */
SELECT CONVERT(varchar(20), aa.appointment_id) AS AppointmentId,
    aa.appointment_no AS AppointmentNo, s.ship_name AS ShipName, s.imo_no AS ImoNo, s.mmsi AS Mmsi,
    TODATETIMEOFFSET(aa.requested_arrival_at, '+00:00') AS RequestedArrivalAt,
    TODATETIMEOFFSET(aa.requested_departure_at, '+00:00') AS RequestedDepartureAt,
    aa.appointment_status_code AS AppointmentStatusCode, reviewer.display_name AS ReviewerName,
    TODATETIMEOFFSET(aa.reviewed_at, '+00:00') AS ReviewedAt, aa.review_comment AS ReviewComment,
    st.item_name AS ShipType, sc.company_name AS CompanyName,
    s.length_m AS LengthM, s.breadth_m AS BreadthM, s.max_draft_m AS MaxDraftM,
    s.gross_tonnage AS GrossTonnage, s.deadweight_t AS DeadweightT, pc.call_no AS CallNo,
    TODATETIMEOFFSET(pc.planned_arrival_at, '+00:00') AS PlannedArrivalAt,
    TODATETIMEOFFSET(pc.planned_departure_at, '+00:00') AS PlannedDepartureAt,
    previous_port.port_name AS PreviousPortName, next_port.port_name AS NextPortName,
    pc.cargo_teu AS CargoTeu, pc.cargo_weight_t AS CargoWeightT,
    aa.estimated_operation_hours AS EstimatedOperationHours, aa.declared_draft_m AS DeclaredDraftM,
    aa.priority_level AS PriorityLevel, aa.dangerous_goods_flag AS DangerousGoodsFlag,
    aa.special_requirement AS SpecialRequirement, TODATETIMEOFFSET(aa.submitted_at, '+00:00') AS SubmittedAt
FROM dbo.arrival_appointment AS aa
INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
INNER JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
INNER JOIN dbo.dictionary_item st ON st.dictionary_item_id=s.ship_type_item_id
INNER JOIN dbo.shipping_company sc ON sc.company_id=s.shipping_company_id
LEFT JOIN dbo.port previous_port ON previous_port.port_id=pc.previous_port_id
LEFT JOIN dbo.port next_port ON next_port.port_id=pc.next_port_id
LEFT JOIN dbo.user_account AS reviewer ON reviewer.user_id = aa.reviewed_by
WHERE aa.appointment_id = @AppointmentId;
