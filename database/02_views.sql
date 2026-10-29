USE [PortQueue];
GO

CREATE OR ALTER VIEW dbo.v_port_event
AS
    SELECT
        e.event_id,
        e.event_type_code,
        e.event_time,
        e.event_source_code,
        e.recorded_by,
        a.queue_entry_id,
        q.appointment_id,
        CAST(NULL AS bigint) AS executed_revision_id
    FROM dbo.port_event AS e
    INNER JOIN dbo.anchorage_event_link AS a ON a.event_id = e.event_id
    INNER JOIN dbo.anchorage_queue_entry AS q ON q.queue_entry_id = a.queue_entry_id

    UNION ALL

    SELECT
        e.event_id,
        e.event_type_code,
        e.event_time,
        e.event_source_code,
        e.recorded_by,
        CAST(NULL AS bigint) AS queue_entry_id,
        p.appointment_id,
        CAST(NULL AS bigint) AS executed_revision_id
    FROM dbo.port_event AS e
    INNER JOIN dbo.port_entry_event_link AS p ON p.event_id = e.event_id

    UNION ALL

    SELECT
        e.event_id,
        e.event_type_code,
        e.event_time,
        e.event_source_code,
        e.recorded_by,
        CAST(NULL AS bigint) AS queue_entry_id,
        bp.appointment_id,
        b.executed_revision_id
    FROM dbo.port_event AS e
    INNER JOIN dbo.berth_event_link AS b ON b.event_id = e.event_id
    INNER JOIN dbo.berth_plan_revision AS r ON r.revision_id = b.executed_revision_id
    INNER JOIN dbo.berth_plan AS bp ON bp.berth_plan_id = r.berth_plan_id;
GO

CREATE OR ALTER VIEW dbo.v_anchorage_queue
AS
    SELECT
        q.queue_entry_id,
        q.appointment_id,
        aa.appointment_no,
        pc.port_call_id,
        s.ship_id,
        s.ship_name,
        q.anchorage_id,
        an.anchorage_name,
        q.queue_position,
        q.priority_level,
        q.queue_status_code,
        enter_event.event_time AS entered_anchorage_at,
        leave_event.event_time AS left_anchorage_at,
        q.estimated_berthing_at,
        q.created_at
    FROM dbo.anchorage_queue_entry AS q
    INNER JOIN dbo.arrival_appointment AS aa ON aa.appointment_id = q.appointment_id
    INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
    INNER JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
    INNER JOIN dbo.anchorage AS an ON an.anchorage_id = q.anchorage_id
    OUTER APPLY
    (
        SELECT TOP (1) e.event_time
        FROM dbo.port_event AS e
        INNER JOIN dbo.anchorage_event_link AS l ON l.event_id = e.event_id
        WHERE l.queue_entry_id = q.queue_entry_id
          AND e.event_type_code = 'ANCHORAGE_ENTER'
        ORDER BY e.event_time ASC, e.event_id ASC
    ) AS enter_event
    OUTER APPLY
    (
        SELECT TOP (1) e.event_time
        FROM dbo.port_event AS e
        INNER JOIN dbo.anchorage_event_link AS l ON l.event_id = e.event_id
        WHERE l.queue_entry_id = q.queue_entry_id
          AND e.event_type_code = 'ANCHORAGE_LEAVE'
        ORDER BY e.event_time ASC, e.event_id ASC
    ) AS leave_event;
GO

CREATE OR ALTER VIEW dbo.v_current_berth_plan
AS
    SELECT
        bp.berth_plan_id,
        bp.appointment_id,
        aa.appointment_no,
        pc.port_call_id,
        s.ship_id,
        s.ship_name,
        bp.plan_status_code,
        r.revision_id,
        r.version_no,
        r.berth_id,
        b.berth_name,
        r.planned_berthing_at,
        r.planned_unberthing_at,
        r.confirmed_by,
        r.confirmed_at
    FROM dbo.berth_plan AS bp
    INNER JOIN dbo.arrival_appointment AS aa ON aa.appointment_id = bp.appointment_id
    INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
    INNER JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
    INNER JOIN dbo.berth_plan_revision AS r ON r.berth_plan_id = bp.berth_plan_id AND r.is_current = 1
    INNER JOIN dbo.berth AS b ON b.berth_id = r.berth_id;
GO
