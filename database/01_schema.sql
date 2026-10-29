USE [PortQueue];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

CREATE TABLE dbo.role
(
    role_code varchar(30) NOT NULL CONSTRAINT PK_role PRIMARY KEY,
    role_name nvarchar(50) NOT NULL CONSTRAINT UQ_role_role_name UNIQUE,
    role_description nvarchar(300) NULL,
    status_code varchar(30) NOT NULL CONSTRAINT CK_role_status CHECK (status_code IN ('ENABLED', 'DISABLED'))
);

CREATE TABLE dbo.dictionary_item
(
    dictionary_item_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_dictionary_item PRIMARY KEY,
    dictionary_type varchar(40) NOT NULL,
    item_code varchar(30) NOT NULL,
    item_name nvarchar(100) NOT NULL,
    sort_no int NOT NULL CONSTRAINT CK_dictionary_item_sort CHECK (sort_no >= 0),
    description nvarchar(300) NULL,
    is_enabled bit NOT NULL CONSTRAINT DF_dictionary_item_is_enabled DEFAULT (1),
    updated_at datetime2(0) NOT NULL CONSTRAINT DF_dictionary_item_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_dictionary_item_type_code UNIQUE (dictionary_type, item_code)
);

CREATE TABLE dbo.shipping_company
(
    company_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_shipping_company PRIMARY KEY,
    company_code varchar(30) NOT NULL CONSTRAINT UQ_shipping_company_code UNIQUE,
    company_name nvarchar(150) NOT NULL,
    contact_name nvarchar(50) NULL,
    contact_phone varchar(30) NULL,
    status_code varchar(30) NOT NULL CONSTRAINT CK_shipping_company_status CHECK (status_code IN ('ACTIVE', 'DISABLED')),
    created_at datetime2(0) NOT NULL CONSTRAINT DF_shipping_company_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at datetime2(0) NOT NULL CONSTRAINT DF_shipping_company_updated_at DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.port
(
    port_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_port PRIMARY KEY,
    port_code varchar(10) NOT NULL CONSTRAINT UQ_port_code UNIQUE,
    port_name nvarchar(100) NOT NULL,
    country_code char(2) NOT NULL,
    timezone varchar(40) NOT NULL,
    longitude decimal(9,6) NULL CONSTRAINT CK_port_longitude CHECK (longitude BETWEEN -180 AND 180),
    latitude decimal(8,6) NULL CONSTRAINT CK_port_latitude CHECK (latitude BETWEEN -90 AND 90),
    status_code varchar(30) NOT NULL CONSTRAINT CK_port_status CHECK (status_code IN ('ENABLED', 'DISABLED'))
);

CREATE TABLE dbo.user_account
(
    user_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_user_account PRIMARY KEY,
    username varchar(50) NOT NULL CONSTRAINT UQ_user_account_username UNIQUE,
    display_name nvarchar(50) NOT NULL,
    role_code varchar(30) NOT NULL,
    phone varchar(30) NULL,
    password_hash varbinary(256) NOT NULL,
    status_code varchar(30) NOT NULL CONSTRAINT CK_user_account_status CHECK (status_code IN ('ENABLED', 'LOCKED', 'DISABLED')),
    created_at datetime2(0) NOT NULL CONSTRAINT DF_user_account_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_user_account_role FOREIGN KEY (role_code) REFERENCES dbo.role(role_code)
);

CREATE TABLE dbo.terminal
(
    terminal_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_terminal PRIMARY KEY,
    port_id bigint NOT NULL,
    terminal_code varchar(30) NOT NULL,
    terminal_name nvarchar(100) NOT NULL,
    terminal_type_code varchar(30) NULL,
    contact_phone varchar(30) NULL,
    status_code varchar(30) NOT NULL CONSTRAINT CK_terminal_status CHECK (status_code IN ('AVAILABLE', 'MAINTENANCE', 'DISABLED')),
    CONSTRAINT UQ_terminal_port_code UNIQUE (port_id, terminal_code),
    CONSTRAINT FK_terminal_port FOREIGN KEY (port_id) REFERENCES dbo.port(port_id)
);

CREATE TABLE dbo.anchorage
(
    anchorage_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_anchorage PRIMARY KEY,
    port_id bigint NOT NULL,
    anchorage_code varchar(30) NOT NULL,
    anchorage_name nvarchar(100) NOT NULL,
    anchorage_type_code varchar(30) NOT NULL,
    capacity_vessel_count int NULL CONSTRAINT CK_anchorage_capacity CHECK (capacity_vessel_count IS NULL OR capacity_vessel_count >= 0),
    longitude decimal(9,6) NULL CONSTRAINT CK_anchorage_longitude CHECK (longitude BETWEEN -180 AND 180),
    latitude decimal(8,6) NULL CONSTRAINT CK_anchorage_latitude CHECK (latitude BETWEEN -90 AND 90),
    status_code varchar(30) NOT NULL CONSTRAINT CK_anchorage_status CHECK (status_code IN ('AVAILABLE', 'RESTRICTED', 'DISABLED')),
    CONSTRAINT UQ_anchorage_port_code UNIQUE (port_id, anchorage_code),
    CONSTRAINT FK_anchorage_port FOREIGN KEY (port_id) REFERENCES dbo.port(port_id)
);

CREATE TABLE dbo.berth
(
    berth_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_berth PRIMARY KEY,
    terminal_id bigint NOT NULL,
    berth_code varchar(30) NOT NULL,
    berth_name nvarchar(100) NOT NULL,
    berth_type_code varchar(30) NULL,
    length_m decimal(7,2) NOT NULL CONSTRAINT CK_berth_length CHECK (length_m > 0),
    max_draft_m decimal(6,2) NOT NULL CONSTRAINT CK_berth_max_draft CHECK (max_draft_m > 0),
    max_dwt decimal(12,2) NULL CONSTRAINT CK_berth_max_dwt CHECK (max_dwt IS NULL OR max_dwt > 0),
    status_code varchar(30) NOT NULL CONSTRAINT CK_berth_status CHECK (status_code IN ('AVAILABLE', 'MAINTENANCE', 'DISABLED')),
    remark nvarchar(500) NULL,
    CONSTRAINT UQ_berth_terminal_code UNIQUE (terminal_id, berth_code),
    CONSTRAINT FK_berth_terminal FOREIGN KEY (terminal_id) REFERENCES dbo.terminal(terminal_id)
);

CREATE TABLE dbo.ship
(
    ship_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ship PRIMARY KEY,
    imo_no varchar(7) NOT NULL CONSTRAINT UQ_ship_imo UNIQUE,
    mmsi char(9) NOT NULL CONSTRAINT UQ_ship_mmsi UNIQUE,
    ship_name nvarchar(100) NOT NULL,
    ship_type_item_id bigint NOT NULL,
    length_m decimal(7,2) NOT NULL CONSTRAINT CK_ship_length CHECK (length_m > 0),
    breadth_m decimal(7,2) NOT NULL CONSTRAINT CK_ship_breadth CHECK (breadth_m > 0),
    max_draft_m decimal(6,2) NOT NULL CONSTRAINT CK_ship_max_draft CHECK (max_draft_m > 0),
    gross_tonnage decimal(12,2) NULL CONSTRAINT CK_ship_gross_tonnage CHECK (gross_tonnage IS NULL OR gross_tonnage >= 0),
    deadweight_t decimal(12,2) NULL CONSTRAINT CK_ship_deadweight CHECK (deadweight_t IS NULL OR deadweight_t > 0),
    shipping_company_id bigint NOT NULL,
    status_code varchar(30) NOT NULL CONSTRAINT CK_ship_status CHECK (status_code IN ('ACTIVE', 'DISABLED')),
    created_at datetime2(0) NOT NULL CONSTRAINT DF_ship_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at datetime2(0) NOT NULL CONSTRAINT DF_ship_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ship_company FOREIGN KEY (shipping_company_id) REFERENCES dbo.shipping_company(company_id),
    CONSTRAINT FK_ship_type FOREIGN KEY (ship_type_item_id) REFERENCES dbo.dictionary_item(dictionary_item_id)
);

CREATE TABLE dbo.berth_ship_type
(
    berth_id bigint NOT NULL,
    ship_type_item_id bigint NOT NULL,
    relation_status varchar(20) NOT NULL CONSTRAINT CK_berth_ship_type_status CHECK (relation_status IN ('ENABLED', 'DISABLED')),
    CONSTRAINT PK_berth_ship_type PRIMARY KEY (berth_id, ship_type_item_id),
    CONSTRAINT FK_berth_ship_type_berth FOREIGN KEY (berth_id) REFERENCES dbo.berth(berth_id),
    CONSTRAINT FK_berth_ship_type_dictionary FOREIGN KEY (ship_type_item_id) REFERENCES dbo.dictionary_item(dictionary_item_id)
);

CREATE TABLE dbo.port_call
(
    port_call_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_port_call PRIMARY KEY,
    call_no varchar(40) NOT NULL,
    ship_id bigint NOT NULL,
    previous_port_id bigint NULL,
    next_port_id bigint NULL,
    planned_arrival_at datetime2(0) NOT NULL,
    planned_departure_at datetime2(0) NULL,
    cargo_teu int NULL CONSTRAINT CK_port_call_cargo_teu CHECK (cargo_teu IS NULL OR cargo_teu >= 0),
    cargo_weight_t decimal(12,2) NULL CONSTRAINT CK_port_call_cargo_weight CHECK (cargo_weight_t IS NULL OR cargo_weight_t >= 0),
    call_status_code varchar(30) NOT NULL CONSTRAINT CK_port_call_status CHECK (call_status_code IN ('PLANNED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')),
    CONSTRAINT UQ_port_call_ship_call_no UNIQUE (ship_id, call_no),
    CONSTRAINT CK_port_call_times CHECK (planned_departure_at IS NULL OR planned_departure_at > planned_arrival_at),
    CONSTRAINT FK_port_call_ship FOREIGN KEY (ship_id) REFERENCES dbo.ship(ship_id),
    CONSTRAINT FK_port_call_previous_port FOREIGN KEY (previous_port_id) REFERENCES dbo.port(port_id),
    CONSTRAINT FK_port_call_next_port FOREIGN KEY (next_port_id) REFERENCES dbo.port(port_id)
);

CREATE TABLE dbo.arrival_appointment
(
    appointment_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_arrival_appointment PRIMARY KEY,
    appointment_no varchar(40) NOT NULL CONSTRAINT UQ_arrival_appointment_no UNIQUE,
    port_call_id bigint NOT NULL CONSTRAINT UQ_arrival_appointment_port_call UNIQUE,
    requested_arrival_at datetime2(0) NOT NULL,
    requested_departure_at datetime2(0) NOT NULL,
    estimated_operation_hours decimal(6,2) NOT NULL CONSTRAINT CK_arrival_appointment_operation_hours CHECK (estimated_operation_hours > 0),
    declared_draft_m decimal(6,2) NOT NULL CONSTRAINT CK_arrival_appointment_draft CHECK (declared_draft_m > 0),
    special_requirement nvarchar(1000) NULL,
    dangerous_goods_flag bit NOT NULL CONSTRAINT DF_arrival_appointment_dangerous_goods DEFAULT (0),
    priority_level tinyint NOT NULL CONSTRAINT CK_arrival_appointment_priority CHECK (priority_level BETWEEN 1 AND 5),
    appointment_status_code varchar(30) NOT NULL CONSTRAINT CK_arrival_appointment_status CHECK (appointment_status_code IN ('DRAFT', 'PENDING', 'APPROVED', 'REJECTED', 'CANCELLED', 'COMPLETED')),
    submitted_at datetime2(0) NULL,
    reviewed_by bigint NULL,
    reviewed_at datetime2(0) NULL,
    review_comment nvarchar(1000) NULL,
    CONSTRAINT CK_arrival_appointment_times CHECK (requested_departure_at > requested_arrival_at),
    CONSTRAINT CK_arrival_appointment_review CHECK ((reviewed_by IS NULL AND reviewed_at IS NULL) OR (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)),
    CONSTRAINT FK_arrival_appointment_port_call FOREIGN KEY (port_call_id) REFERENCES dbo.port_call(port_call_id),
    CONSTRAINT FK_arrival_appointment_reviewer FOREIGN KEY (reviewed_by) REFERENCES dbo.user_account(user_id)
);

CREATE TABLE dbo.anchorage_queue_entry
(
    queue_entry_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_anchorage_queue_entry PRIMARY KEY,
    appointment_id bigint NOT NULL,
    anchorage_id bigint NOT NULL,
    queue_position int NULL CONSTRAINT CK_queue_position CHECK (queue_position IS NULL OR queue_position > 0),
    priority_level tinyint NOT NULL CONSTRAINT CK_queue_priority CHECK (priority_level BETWEEN 1 AND 5),
    queue_status_code varchar(30) NOT NULL CONSTRAINT CK_queue_status CHECK (queue_status_code IN ('WAITING', 'ASSIGNED', 'LEFT', 'CANCELLED')),
    estimated_berthing_at datetime2(0) NULL,
    position_updated_at datetime2(0) NULL,
    created_at datetime2(0) NOT NULL CONSTRAINT DF_queue_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_queue_position_by_status CHECK ((queue_status_code IN ('WAITING', 'ASSIGNED') AND queue_position IS NOT NULL) OR (queue_status_code IN ('LEFT', 'CANCELLED') AND queue_position IS NULL)),
    CONSTRAINT FK_queue_appointment FOREIGN KEY (appointment_id) REFERENCES dbo.arrival_appointment(appointment_id),
    CONSTRAINT FK_queue_anchorage FOREIGN KEY (anchorage_id) REFERENCES dbo.anchorage(anchorage_id)
);

CREATE TABLE dbo.queue_adjustment_record
(
    adjustment_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_queue_adjustment_record PRIMARY KEY,
    queue_entry_id bigint NOT NULL,
    old_position int NOT NULL CONSTRAINT CK_queue_adjustment_old_position CHECK (old_position > 0),
    new_position int NOT NULL CONSTRAINT CK_queue_adjustment_new_position CHECK (new_position > 0),
    adjustment_reason nvarchar(500) NOT NULL,
    adjusted_by bigint NOT NULL,
    adjusted_at datetime2(0) NOT NULL CONSTRAINT DF_queue_adjustment_at DEFAULT (SYSUTCDATETIME()),
    remark nvarchar(500) NULL,
    CONSTRAINT FK_queue_adjustment_queue FOREIGN KEY (queue_entry_id) REFERENCES dbo.anchorage_queue_entry(queue_entry_id),
    CONSTRAINT FK_queue_adjustment_user FOREIGN KEY (adjusted_by) REFERENCES dbo.user_account(user_id)
);

CREATE TABLE dbo.berth_plan
(
    berth_plan_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_berth_plan PRIMARY KEY,
    appointment_id bigint NOT NULL CONSTRAINT UQ_berth_plan_appointment UNIQUE,
    plan_status_code varchar(30) NOT NULL CONSTRAINT CK_berth_plan_status CHECK (plan_status_code IN ('DRAFT', 'CONFIRMED', 'EXECUTING', 'COMPLETED', 'CANCELLED')),
    cancelled_at datetime2(0) NULL,
    cancel_reason nvarchar(500) NULL,
    created_at datetime2(0) NOT NULL CONSTRAINT DF_berth_plan_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at datetime2(0) NOT NULL CONSTRAINT DF_berth_plan_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_berth_plan_cancel CHECK ((cancelled_at IS NULL AND cancel_reason IS NULL) OR (cancelled_at IS NOT NULL AND cancel_reason IS NOT NULL)),
    CONSTRAINT FK_berth_plan_appointment FOREIGN KEY (appointment_id) REFERENCES dbo.arrival_appointment(appointment_id)
);

CREATE TABLE dbo.berth_plan_revision
(
    revision_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_berth_plan_revision PRIMARY KEY,
    berth_plan_id bigint NOT NULL,
    version_no int NOT NULL CONSTRAINT CK_revision_version_no CHECK (version_no > 0),
    berth_id bigint NOT NULL,
    planned_berthing_at datetime2(0) NOT NULL,
    planned_unberthing_at datetime2(0) NOT NULL,
    revision_reason nvarchar(500) NOT NULL,
    revised_by bigint NOT NULL,
    revised_at datetime2(0) NOT NULL CONSTRAINT DF_revision_revised_at DEFAULT (SYSUTCDATETIME()),
    confirmed_by bigint NULL,
    confirmed_at datetime2(0) NULL,
    is_current bit NOT NULL CONSTRAINT DF_revision_is_current DEFAULT (0),
    CONSTRAINT UQ_revision_plan_version UNIQUE (berth_plan_id, version_no),
    CONSTRAINT CK_revision_times CHECK (planned_unberthing_at > planned_berthing_at),
    CONSTRAINT CK_revision_confirmation CHECK ((confirmed_by IS NULL AND confirmed_at IS NULL) OR (confirmed_by IS NOT NULL AND confirmed_at IS NOT NULL)),
    CONSTRAINT FK_revision_plan FOREIGN KEY (berth_plan_id) REFERENCES dbo.berth_plan(berth_plan_id),
    CONSTRAINT FK_revision_berth FOREIGN KEY (berth_id) REFERENCES dbo.berth(berth_id),
    CONSTRAINT FK_revision_revised_by FOREIGN KEY (revised_by) REFERENCES dbo.user_account(user_id),
    CONSTRAINT FK_revision_confirmed_by FOREIGN KEY (confirmed_by) REFERENCES dbo.user_account(user_id)
);

CREATE TABLE dbo.port_event
(
    event_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_port_event PRIMARY KEY,
    event_type_code varchar(30) NOT NULL CONSTRAINT CK_port_event_type CHECK (event_type_code IN ('ANCHORAGE_ENTER', 'ANCHORAGE_LEAVE', 'PORT_ENTER', 'BERTHING', 'UNBERTHING')),
    event_time datetime2(0) NOT NULL,
    event_source_code varchar(30) NOT NULL CONSTRAINT CK_port_event_source CHECK (event_source_code IN ('MANUAL', 'INTERFACE', 'SENSOR')),
    recorded_by bigint NULL,
    remark nvarchar(500) NULL,
    created_at datetime2(0) NOT NULL CONSTRAINT DF_port_event_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_port_event_created_before_event CHECK (event_time <= created_at),
    CONSTRAINT FK_port_event_recorder FOREIGN KEY (recorded_by) REFERENCES dbo.user_account(user_id)
);

CREATE TABLE dbo.audit_log
(
    audit_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_audit_log PRIMARY KEY,
    table_name varchar(100) NOT NULL,
    record_id varchar(100) NOT NULL,
    operation_type varchar(20) NOT NULL CONSTRAINT CK_audit_operation CHECK (operation_type IN ('INSERT', 'UPDATE', 'DELETE')),
    old_data_json nvarchar(max) NULL,
    new_data_json nvarchar(max) NULL,
    operator_id bigint NOT NULL,
    operated_at datetime2(0) NOT NULL CONSTRAINT DF_audit_operated_at DEFAULT (SYSUTCDATETIME()),
    operation_ip varchar(45) NULL,
    CONSTRAINT CK_audit_old_json CHECK (old_data_json IS NULL OR ISJSON(old_data_json) = 1),
    CONSTRAINT CK_audit_new_json CHECK (new_data_json IS NULL OR ISJSON(new_data_json) = 1),
    CONSTRAINT FK_audit_operator FOREIGN KEY (operator_id) REFERENCES dbo.user_account(user_id)
);

CREATE TABLE dbo.anchorage_event_link
(
    event_id bigint NOT NULL CONSTRAINT PK_anchorage_event_link PRIMARY KEY,
    queue_entry_id bigint NOT NULL,
    CONSTRAINT FK_anchorage_event_link_event FOREIGN KEY (event_id) REFERENCES dbo.port_event(event_id),
    CONSTRAINT FK_anchorage_event_link_queue FOREIGN KEY (queue_entry_id) REFERENCES dbo.anchorage_queue_entry(queue_entry_id)
);

CREATE TABLE dbo.port_entry_event_link
(
    event_id bigint NOT NULL CONSTRAINT PK_port_entry_event_link PRIMARY KEY,
    appointment_id bigint NOT NULL,
    CONSTRAINT UQ_port_entry_event_link_appointment UNIQUE (appointment_id),
    CONSTRAINT FK_port_entry_event_link_event FOREIGN KEY (event_id) REFERENCES dbo.port_event(event_id),
    CONSTRAINT FK_port_entry_event_link_appointment FOREIGN KEY (appointment_id) REFERENCES dbo.arrival_appointment(appointment_id)
);

CREATE TABLE dbo.berth_event_link
(
    event_id bigint NOT NULL CONSTRAINT PK_berth_event_link PRIMARY KEY,
    executed_revision_id bigint NOT NULL,
    CONSTRAINT FK_berth_event_link_event FOREIGN KEY (event_id) REFERENCES dbo.port_event(event_id),
    CONSTRAINT FK_berth_event_link_revision FOREIGN KEY (executed_revision_id) REFERENCES dbo.berth_plan_revision(revision_id)
);
GO

CREATE UNIQUE INDEX UX_queue_active_position
    ON dbo.anchorage_queue_entry(anchorage_id, queue_position)
    WHERE queue_status_code IN ('WAITING', 'ASSIGNED') AND queue_position IS NOT NULL;

CREATE UNIQUE INDEX UX_revision_current_per_plan
    ON dbo.berth_plan_revision(berth_plan_id)
    WHERE is_current = 1;

CREATE INDEX IX_port_call_planned_arrival ON dbo.port_call(planned_arrival_at, call_status_code);
CREATE INDEX IX_appointment_status_arrival ON dbo.arrival_appointment(appointment_status_code, requested_arrival_at);
CREATE INDEX IX_queue_active_anchorage ON dbo.anchorage_queue_entry(anchorage_id, queue_status_code, queue_position);
CREATE INDEX IX_revision_berth_time ON dbo.berth_plan_revision(berth_id, planned_berthing_at, planned_unberthing_at);
CREATE INDEX IX_event_time_type ON dbo.port_event(event_time, event_type_code);
CREATE INDEX IX_audit_record_time ON dbo.audit_log(table_name, record_id, operated_at DESC);
GO
