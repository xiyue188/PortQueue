USE [PortQueue];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_review_appointment
    @appointment_id bigint,
    @reviewer_id bigint,
    @decision varchar(20),
    @review_comment nvarchar(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @decision NOT IN ('APPROVED', 'REJECTED')
        THROW 52001, 'Decision must be APPROVED or REJECTED.', 1;

    BEGIN TRANSACTION;

    DECLARE @current_status varchar(30);
    SELECT @current_status = appointment_status_code
    FROM dbo.arrival_appointment WITH (UPDLOCK, HOLDLOCK)
    WHERE appointment_id = @appointment_id;

    IF @current_status IS NULL
        THROW 52002, 'Appointment does not exist.', 1;
    IF @current_status NOT IN ('PENDING', 'REJECTED')
        THROW 52003, 'Appointment status does not allow review.', 1;

    UPDATE dbo.arrival_appointment
    SET appointment_status_code = @decision,
        reviewed_by = @reviewer_id,
        reviewed_at = SYSUTCDATETIME(),
        review_comment = @review_comment,
        submitted_at = COALESCE(submitted_at, SYSUTCDATETIME())
    WHERE appointment_id = @appointment_id;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_confirm_berth_plan
    @berth_plan_id bigint,
    @revision_id bigint,
    @operator_id bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    BEGIN TRANSACTION;

    DECLARE @appointment_id bigint, @ship_id bigint, @berth_id bigint,
            @start_at datetime2(0), @end_at datetime2(0), @plan_status varchar(30);

    SELECT
        @appointment_id = bp.appointment_id,
        @plan_status = bp.plan_status_code,
        @ship_id = pc.ship_id,
        @berth_id = r.berth_id,
        @start_at = r.planned_berthing_at,
        @end_at = r.planned_unberthing_at
    FROM dbo.berth_plan AS bp WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.arrival_appointment AS aa ON aa.appointment_id = bp.appointment_id
    INNER JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
    INNER JOIN dbo.berth_plan_revision AS r WITH (UPDLOCK, HOLDLOCK)
        ON r.revision_id = @revision_id AND r.berth_plan_id = bp.berth_plan_id
    WHERE bp.berth_plan_id = @berth_plan_id;

    IF @appointment_id IS NULL
        THROW 52010, 'Berth plan or revision does not exist.', 1;
    IF @plan_status IN ('CANCELLED', 'COMPLETED')
        THROW 52011, 'Plan status does not allow confirmation.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.arrival_appointment
        WHERE appointment_id = @appointment_id AND appointment_status_code = 'APPROVED'
    )
        THROW 52012, 'Only an approved appointment can confirm a berth plan.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.berth AS b
        INNER JOIN dbo.ship AS s ON s.ship_id = @ship_id
        WHERE b.berth_id = @berth_id
          AND b.status_code = 'AVAILABLE'
          AND b.length_m >= s.length_m
          AND b.max_draft_m >= (SELECT declared_draft_m FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id)
          AND (b.max_dwt IS NULL OR (s.deadweight_t IS NOT NULL AND b.max_dwt >= s.deadweight_t))
    )
        THROW 52013, 'Ship dimensions or berth capability check failed.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.berth_ship_type AS bst
        INNER JOIN dbo.ship AS s ON s.ship_id = @ship_id AND s.ship_type_item_id = bst.ship_type_item_id
        WHERE bst.berth_id = @berth_id AND bst.relation_status = 'ENABLED'
    )
        THROW 52014, 'Berth does not enable this ship type.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.berth_plan_revision AS other WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.berth_plan AS other_plan ON other_plan.berth_plan_id = other.berth_plan_id
        WHERE other.berth_id = @berth_id
          AND other.is_current = 1
          AND other_plan.plan_status_code IN ('CONFIRMED', 'EXECUTING')
          AND other_plan.berth_plan_id <> @berth_plan_id
          AND other.planned_berthing_at < @end_at
          AND other.planned_unberthing_at > @start_at
    )
        THROW 52015, 'The berth time window conflicts with another plan.', 1;
    UPDATE dbo.berth_plan_revision
    SET is_current = 0
    WHERE berth_plan_id = @berth_plan_id AND is_current = 1;

    UPDATE dbo.berth_plan_revision
    SET is_current = 1,
        confirmed_by = @operator_id,
        confirmed_at = SYSUTCDATETIME()
    WHERE revision_id = @revision_id;

    UPDATE dbo.berth_plan
    SET plan_status_code = 'CONFIRMED',
        updated_at = SYSUTCDATETIME()
    WHERE berth_plan_id = @berth_plan_id;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_record_anchorage_event
    @appointment_id bigint,
    @anchorage_id bigint,
    @event_type_code varchar(30),
    @event_time datetime2(0),
    @operator_id bigint,
    @remark nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @event_type_code NOT IN ('ANCHORAGE_ENTER', 'ANCHORAGE_LEAVE')
        THROW 52020, 'Anchorage operation must be enter or leave.', 1;

    BEGIN TRANSACTION;

    DECLARE @queue_entry_id bigint, @previous_event_time datetime2(0), @status varchar(30);

    SELECT TOP (1)
        @queue_entry_id = q.queue_entry_id,
        @status = q.queue_status_code
    FROM dbo.anchorage_queue_entry AS q WITH (UPDLOCK, HOLDLOCK)
    WHERE q.appointment_id = @appointment_id
      AND q.queue_status_code IN ('WAITING', 'ASSIGNED')
    ORDER BY q.queue_entry_id DESC;

    IF @event_type_code = 'ANCHORAGE_ENTER'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id AND appointment_status_code = 'APPROVED')
            THROW 52021, 'Only an approved appointment can enter anchorage.', 1;
        IF @queue_entry_id IS NOT NULL
            THROW 52022, 'Appointment already has an active anchorage stay.', 1;
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.anchorage
            WHERE anchorage_id = @anchorage_id AND status_code = 'AVAILABLE'
        )
            THROW 52025, 'Target anchorage is not available.', 1;

        INSERT dbo.anchorage_queue_entry
        (
            appointment_id, anchorage_id, queue_position, priority_level,
            queue_status_code, position_updated_at
        )
        VALUES
        (
            @appointment_id, @anchorage_id,
            (SELECT COALESCE(MAX(queue_position), 0) + 1 FROM dbo.anchorage_queue_entry WITH (UPDLOCK, HOLDLOCK) WHERE anchorage_id = @anchorage_id AND queue_status_code IN ('WAITING', 'ASSIGNED')),
            (SELECT priority_level FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id),
            'WAITING', SYSUTCDATETIME()
        );
        SET @queue_entry_id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @queue_entry_id IS NULL
            THROW 52023, 'Appointment has no active anchorage stay.', 1;
        SELECT TOP (1) @previous_event_time = e.event_time
        FROM dbo.port_event AS e
        INNER JOIN dbo.anchorage_event_link AS l ON l.event_id = e.event_id
        WHERE l.queue_entry_id = @queue_entry_id AND e.event_type_code = 'ANCHORAGE_ENTER'
        ORDER BY e.event_time ASC, e.event_id ASC;
        IF @previous_event_time IS NULL OR @event_time <= @previous_event_time
            THROW 52024, 'Anchorage leave time must be after enter time.', 1;
    END;

    INSERT dbo.port_event(event_type_code, event_time, event_source_code, recorded_by, remark)
    VALUES (@event_type_code, @event_time, 'MANUAL', @operator_id, @remark);
    DECLARE @event_id bigint = SCOPE_IDENTITY();

    INSERT dbo.anchorage_event_link(event_id, queue_entry_id)
    VALUES (@event_id, @queue_entry_id);

    IF @event_type_code = 'ANCHORAGE_LEAVE'
    BEGIN
        UPDATE dbo.anchorage_queue_entry
        SET queue_status_code = 'LEFT', queue_position = NULL, position_updated_at = SYSUTCDATETIME()
        WHERE queue_entry_id = @queue_entry_id;
    END;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_record_port_entry
    @appointment_id bigint,
    @event_time datetime2(0),
    @operator_id bigint,
    @remark nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.arrival_appointment WITH (UPDLOCK, HOLDLOCK)
        WHERE appointment_id = @appointment_id AND appointment_status_code = 'APPROVED'
    )
        THROW 52040, 'Only an approved appointment can enter the port.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.port_entry_event_link AS l
        WHERE l.appointment_id = @appointment_id
    )
        THROW 52041, 'This port call already has a port entry event.', 1;

    INSERT dbo.port_event(event_type_code, event_time, event_source_code, recorded_by, remark)
    VALUES ('PORT_ENTER', @event_time, 'MANUAL', @operator_id, @remark);
    DECLARE @event_id bigint = SCOPE_IDENTITY();
    INSERT dbo.port_entry_event_link(event_id, appointment_id)
    VALUES (@event_id, @appointment_id);

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_record_berth_event
    @appointment_id bigint,
    @event_type_code varchar(30),
    @event_time datetime2(0),
    @executed_revision_id bigint,
    @operator_id bigint,
    @remark nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @event_type_code NOT IN ('BERTHING', 'UNBERTHING')
        THROW 52030, 'Berth operation must be berthing or unberthing.', 1;

    BEGIN TRANSACTION;

    DECLARE @plan_id bigint, @existing_event_time datetime2(0);
    SELECT @plan_id = bp.berth_plan_id
    FROM dbo.berth_plan AS bp WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.berth_plan_revision AS r ON r.berth_plan_id = bp.berth_plan_id AND r.revision_id = @executed_revision_id
    WHERE bp.appointment_id = @appointment_id AND bp.plan_status_code IN ('CONFIRMED', 'EXECUTING')
      AND r.confirmed_by IS NOT NULL AND r.confirmed_at IS NOT NULL
      AND (bp.plan_status_code = 'EXECUTING' OR r.is_current = 1);

    IF @plan_id IS NULL
        THROW 52031, 'Appointment has no confirmed executable berth revision.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.port_event AS e
        INNER JOIN dbo.berth_event_link AS l ON l.event_id = e.event_id
        INNER JOIN dbo.berth_plan_revision AS r ON r.revision_id = l.executed_revision_id
        INNER JOIN dbo.berth_plan AS bp ON bp.berth_plan_id = r.berth_plan_id
        WHERE bp.appointment_id = @appointment_id AND e.event_type_code = @event_type_code
    )
        THROW 52032, 'Appointment already has this berth event type.', 1;

    IF @event_type_code = 'UNBERTHING'
    BEGIN
        SELECT TOP (1) @existing_event_time = e.event_time
        FROM dbo.port_event AS e
        INNER JOIN dbo.berth_event_link AS l ON l.event_id = e.event_id
        INNER JOIN dbo.berth_plan_revision AS r ON r.revision_id = l.executed_revision_id
        INNER JOIN dbo.berth_plan AS bp ON bp.berth_plan_id = r.berth_plan_id
        WHERE bp.appointment_id = @appointment_id AND e.event_type_code = 'BERTHING'
        ORDER BY e.event_time ASC, e.event_id ASC;
        IF @existing_event_time IS NULL OR @event_time <= @existing_event_time
            THROW 52033, 'Unberthing time must be after berthing time.', 1;
        IF EXISTS
        (
            SELECT 1
            FROM dbo.port_event AS e
            INNER JOIN dbo.berth_event_link AS l ON l.event_id = e.event_id
            INNER JOIN dbo.berth_plan_revision AS r ON r.revision_id = l.executed_revision_id
            INNER JOIN dbo.berth_plan AS bp ON bp.berth_plan_id = r.berth_plan_id
            WHERE bp.appointment_id = @appointment_id
              AND e.event_type_code = 'BERTHING'
              AND l.executed_revision_id <> @executed_revision_id
        )
            THROW 52034, 'Unberthing must use the same execution baseline as berthing.', 1;
    END;

    INSERT dbo.port_event(event_type_code, event_time, event_source_code, recorded_by, remark)
    VALUES (@event_type_code, @event_time, 'MANUAL', @operator_id, @remark);
    DECLARE @event_id bigint = SCOPE_IDENTITY();

    INSERT dbo.berth_event_link(event_id, executed_revision_id)
    VALUES (@event_id, @executed_revision_id);

    IF @event_type_code = 'BERTHING'
    BEGIN
        UPDATE dbo.berth_plan SET plan_status_code = 'EXECUTING', updated_at = SYSUTCDATETIME() WHERE berth_plan_id = @plan_id;
        UPDATE dbo.port_call SET call_status_code = 'IN_PROGRESS'
        WHERE port_call_id = (SELECT port_call_id FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id);
    END
    ELSE
    BEGIN
        UPDATE dbo.berth_plan SET plan_status_code = 'COMPLETED', updated_at = SYSUTCDATETIME() WHERE berth_plan_id = @plan_id;
        UPDATE dbo.arrival_appointment SET appointment_status_code = 'COMPLETED' WHERE appointment_id = @appointment_id;
        UPDATE dbo.port_call SET call_status_code = 'COMPLETED'
        WHERE port_call_id = (SELECT port_call_id FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id);
    END;

    COMMIT TRANSACTION;
END;
GO
