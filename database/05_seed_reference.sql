/*
   模块：基础参考数据。
   作用：写入可重复执行的角色、字典、港口、锚地、泊位等演示前置数据。
   原则：只提供演示所需的稳定参考数据，不伪造业务执行结果。
*/
USE [PortQueue];
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

MERGE dbo.role AS target
USING (VALUES
    ('ADMIN', N'Administrator', N'Maintain reference data and accounts'),
    ('DISPATCHER', N'Dispatcher', N'Maintain queues, berth plans and execution'),
    ('REVIEWER', N'Reviewer', N'Review arrival appointments'),
    ('RECORDER', N'Recorder', N'Record events and query results')
) AS source(role_code, role_name, role_description)
ON target.role_code = source.role_code
WHEN MATCHED THEN UPDATE SET role_name = source.role_name, role_description = source.role_description, status_code = 'ENABLED'
WHEN NOT MATCHED THEN INSERT (role_code, role_name, role_description, status_code) VALUES (source.role_code, source.role_name, source.role_description, 'ENABLED');

MERGE dbo.dictionary_item AS target
USING (VALUES
    ('SHIP_TYPE', 'CONTAINER', N'Container ship', 10),
    ('SHIP_TYPE', 'BULK', N'Bulk carrier', 20),
    ('SHIP_TYPE', 'TANKER', N'Tanker', 30),
    ('SHIP_TYPE', 'GENERAL', N'General cargo', 40),
    ('TERMINAL_TYPE', 'CONTAINER', N'Container terminal', 10),
    ('ANCHORAGE_TYPE', 'OUTER', N'Outer anchorage', 10),
    ('ANCHORAGE_TYPE', 'INNER', N'Inner anchorage', 20),
    ('BERTH_TYPE', 'CONTAINER', N'Container berth', 10)
) AS source(dictionary_type, item_code, item_name, sort_no)
ON target.dictionary_type = source.dictionary_type AND target.item_code = source.item_code
WHEN MATCHED THEN UPDATE SET item_name = source.item_name, sort_no = source.sort_no, is_enabled = 1, updated_at = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (dictionary_type, item_code, item_name, sort_no, is_enabled) VALUES (source.dictionary_type, source.item_code, source.item_name, source.sort_no, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.port WHERE port_code = 'CNTAO')
    INSERT dbo.port(port_code, port_name, country_code, timezone, status_code) VALUES ('CNTAO', N'Qingdao Port', 'CN', 'Asia/Shanghai', 'ENABLED');

IF NOT EXISTS (SELECT 1 FROM dbo.shipping_company WHERE company_code = 'DEMO-SC')
    INSERT dbo.shipping_company(company_code, company_name, status_code) VALUES ('DEMO-SC', N'Demo Ocean Shipping', 'ACTIVE');

DECLARE @port_id bigint = (SELECT port_id FROM dbo.port WHERE port_code = 'CNTAO');
IF @port_id IS NULL
    THROW 53010, 'Reference port CNTAO is required before seeding facilities.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.terminal WHERE port_id = @port_id AND terminal_code = 'T-A')
    INSERT dbo.terminal(port_id, terminal_code, terminal_name, terminal_type_code, status_code)
    VALUES (@port_id, 'T-A', N'Container Terminal A', 'CONTAINER', 'AVAILABLE');

IF NOT EXISTS (SELECT 1 FROM dbo.anchorage WHERE port_id = @port_id AND anchorage_code = 'ANCH-OUTER-A')
    INSERT dbo.anchorage(port_id, anchorage_code, anchorage_name, anchorage_type_code, capacity_vessel_count, longitude, latitude, status_code)
    VALUES (@port_id, 'ANCH-OUTER-A', N'Outer Anchorage A', 'OUTER', 20, 120.320000, 36.080000, 'AVAILABLE');

IF NOT EXISTS (SELECT 1 FROM dbo.anchorage WHERE port_id = @port_id AND anchorage_code = 'ANCH-INNER-A')
    INSERT dbo.anchorage(port_id, anchorage_code, anchorage_name, anchorage_type_code, capacity_vessel_count, longitude, latitude, status_code)
    VALUES (@port_id, 'ANCH-INNER-A', N'Inner Anchorage A', 'INNER', 10, 120.300000, 36.090000, 'AVAILABLE');

DECLARE @terminal_id bigint = (SELECT terminal_id FROM dbo.terminal WHERE port_id = @port_id AND terminal_code = 'T-A');
IF NOT EXISTS (SELECT 1 FROM dbo.berth WHERE terminal_id = @terminal_id AND berth_code = 'B-01')
    INSERT dbo.berth(terminal_id, berth_code, berth_name, berth_type_code, length_m, max_draft_m, max_dwt, status_code)
    VALUES (@terminal_id, 'B-01', N'Berth 01', 'CONTAINER', 350.00, 15.00, 120000.00, 'AVAILABLE');

DECLARE @berth_id bigint = (SELECT berth_id FROM dbo.berth WHERE terminal_id = @terminal_id AND berth_code = 'B-01');
DECLARE @container_type_id bigint = (SELECT dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type = 'SHIP_TYPE' AND item_code = 'CONTAINER');
IF NOT EXISTS (SELECT 1 FROM dbo.berth_ship_type WHERE berth_id = @berth_id AND ship_type_item_id = @container_type_id)
    INSERT dbo.berth_ship_type(berth_id, ship_type_item_id, relation_status)
    VALUES (@berth_id, @container_type_id, 'ENABLED');

IF NOT EXISTS (SELECT 1 FROM dbo.user_account WHERE username = 'demo.dispatcher')
    INSERT dbo.user_account(username, display_name, role_code, password_hash, status_code)
    VALUES ('demo.dispatcher', N'Demo Dispatcher', 'DISPATCHER', HASHBYTES('SHA2_256', CONVERT(varbinary(4000), 'PortQueue-Demo-Only')), 'ENABLED');

COMMIT TRANSACTION;
GO
