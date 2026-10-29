USE [PortQueue];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @port_id bigint = (SELECT TOP (1) port_id FROM dbo.port WHERE port_code = 'CNTAO');
DECLARE @bad_type_id bigint = (SELECT TOP (1) dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type = 'TERMINAL_TYPE');
DECLARE @passed int = 0;

INSERT dbo.terminal(port_id, terminal_code, terminal_name, status_code)
VALUES (@port_id, 'NEG-T', N'Negative Terminal', 'AVAILABLE');
DECLARE @terminal_id bigint = SCOPE_IDENTITY();

INSERT dbo.berth(terminal_id, berth_code, berth_name, length_m, max_draft_m, status_code)
VALUES (@terminal_id, 'NEG-B', N'Negative Berth', 200, 12, 'AVAILABLE');
DECLARE @berth_id bigint = SCOPE_IDENTITY();

BEGIN TRY
    INSERT dbo.berth_ship_type(berth_id, ship_type_item_id, relation_status)
    VALUES (@berth_id, @bad_type_id, 'ENABLED');
END TRY
BEGIN CATCH
    SET @passed = @passed + CASE WHEN ERROR_NUMBER() = 51002 THEN 1 ELSE 0 END;
END CATCH;

IF @passed <> 1
    THROW 53101, 'Wrong dictionary subtype was not rejected.', 1;

SELECT 'wrong_dictionary_subtype_rejected' AS check_name, @passed AS passed;
ROLLBACK TRANSACTION;
GO
