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

IF NOT EXISTS (SELECT 1 FROM dbo.user_account WHERE username = 'demo.dispatcher')
    INSERT dbo.user_account(username, display_name, role_code, password_hash, status_code)
    VALUES ('demo.dispatcher', N'Demo Dispatcher', 'DISPATCHER', HASHBYTES('SHA2_256', CONVERT(varbinary(4000), 'PortQueue-Demo-Only')), 'ENABLED');

COMMIT TRANSACTION;
GO
