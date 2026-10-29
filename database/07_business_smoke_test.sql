/*
   模块：核心业务正向冒烟测试。
   作用：在可回滚事务中贯通审核、锚地、计划、进港和靠离泊链路。
   结果：输出状态和记录数量，测试结束回滚业务数据。
*/
USE [PortQueue];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

/*
   This is a reversible valid-path test. The procedures commit their nested
   transactions, while the outer transaction rolls back all test records at
   the end. Reference seed data remains unchanged.
*/
BEGIN TRANSACTION;

DECLARE @company_id bigint = (SELECT TOP (1) company_id FROM dbo.shipping_company WHERE company_code = 'DEMO-SC');
DECLARE @user_id bigint = (SELECT TOP (1) user_id FROM dbo.user_account WHERE username = 'demo.dispatcher');
DECLARE @port_id bigint = (SELECT TOP (1) port_id FROM dbo.port WHERE port_code = 'CNTAO');
DECLARE @ship_type_item_id bigint = (SELECT TOP (1) dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type = 'SHIP_TYPE' AND item_code = 'CONTAINER');

IF @company_id IS NULL OR @user_id IS NULL OR @port_id IS NULL OR @ship_type_item_id IS NULL
    THROW 53001, 'Seed data is incomplete.', 1;

INSERT dbo.terminal(port_id, terminal_code, terminal_name, status_code)
VALUES (@port_id, 'SMOKE-T', N'Smoke Terminal', 'AVAILABLE');
DECLARE @terminal_id bigint = SCOPE_IDENTITY();

INSERT dbo.anchorage(port_id, anchorage_code, anchorage_name, anchorage_type_code, capacity_vessel_count, status_code)
VALUES (@port_id, 'SMOKE-A', N'Smoke Anchorage', 'OUTER', 10, 'AVAILABLE');
DECLARE @anchorage_id bigint = SCOPE_IDENTITY();

INSERT dbo.berth(terminal_id, berth_code, berth_name, berth_type_code, length_m, max_draft_m, max_dwt, status_code)
VALUES (@terminal_id, 'SMOKE-B', N'Smoke Berth', 'CONTAINER', 300, 15, 100000, 'AVAILABLE');
DECLARE @berth_id bigint = SCOPE_IDENTITY();

INSERT dbo.berth_ship_type(berth_id, ship_type_item_id, relation_status)
VALUES (@berth_id, @ship_type_item_id, 'ENABLED');

INSERT dbo.ship(imo_no, mmsi, ship_name, ship_type_item_id, length_m, breadth_m, max_draft_m, deadweight_t, shipping_company_id, status_code)
VALUES ('SMOKE01', '123456789', N'Smoke Ship', @ship_type_item_id, 200, 30, 10, 50000, @company_id, 'ACTIVE');
DECLARE @ship_id bigint = SCOPE_IDENTITY();

INSERT dbo.port_call(call_no, ship_id, planned_arrival_at, planned_departure_at, call_status_code)
VALUES ('SMOKE-001', @ship_id, '2026-10-27T00:00:00', '2026-10-27T10:00:00', 'PLANNED');
DECLARE @port_call_id bigint = SCOPE_IDENTITY();

INSERT dbo.arrival_appointment
(
    appointment_no, port_call_id, requested_arrival_at, requested_departure_at,
    estimated_operation_hours, declared_draft_m, priority_level,
    appointment_status_code, submitted_at
)
VALUES
(
    'SMOKE-AP-001', @port_call_id, '2026-10-27T00:30:00', '2026-10-27T08:00:00',
    5, 9, 3, 'PENDING', '2026-10-26T12:00:00'
);
DECLARE @appointment_id bigint = SCOPE_IDENTITY();

EXEC dbo.usp_review_appointment @appointment_id, @user_id, 'APPROVED', N'Smoke test approved';
EXEC dbo.usp_record_anchorage_event @appointment_id, @anchorage_id, 'ANCHORAGE_ENTER', '2026-10-27T01:00:00', @user_id, NULL;
EXEC dbo.usp_record_anchorage_event @appointment_id, @anchorage_id, 'ANCHORAGE_LEAVE', '2026-10-27T02:00:00', @user_id, NULL;

INSERT dbo.berth_plan(appointment_id, plan_status_code)
VALUES (@appointment_id, 'DRAFT');
DECLARE @berth_plan_id bigint = SCOPE_IDENTITY();

INSERT dbo.berth_plan_revision
(
    berth_plan_id, version_no, berth_id, planned_berthing_at,
    planned_unberthing_at, revision_reason, revised_by, is_current
)
VALUES
(
    @berth_plan_id, 1, @berth_id, '2026-10-27T03:00:00',
    '2026-10-27T05:00:00', N'Initial plan', @user_id, 0
);
DECLARE @revision_id bigint = SCOPE_IDENTITY();

EXEC dbo.usp_confirm_berth_plan @berth_plan_id, @revision_id, @user_id;
EXEC dbo.usp_record_port_entry @appointment_id, '2026-10-27T02:10:00', @user_id, NULL;
EXEC dbo.usp_record_berth_event @appointment_id, 'BERTHING', '2026-10-27T03:10:00', @revision_id, @user_id, NULL;
EXEC dbo.usp_record_berth_event @appointment_id, 'UNBERTHING', '2026-10-27T05:10:00', @revision_id, @user_id, NULL;

SELECT
    (SELECT appointment_status_code FROM dbo.arrival_appointment WHERE appointment_id = @appointment_id) AS appointment_status,
    (SELECT queue_status_code FROM dbo.anchorage_queue_entry WHERE appointment_id = @appointment_id) AS queue_status,
    (SELECT plan_status_code FROM dbo.berth_plan WHERE berth_plan_id = @berth_plan_id) AS plan_status,
    (SELECT COUNT(*) FROM dbo.v_port_event WHERE appointment_id = @appointment_id) AS event_count,
    (SELECT COUNT(*) FROM dbo.v_anchorage_queue WHERE appointment_id = @appointment_id) AS queue_view_rows,
    (SELECT COUNT(*) FROM dbo.v_current_berth_plan WHERE appointment_id = @appointment_id) AS current_plan_view_rows;

ROLLBACK TRANSACTION;
GO
