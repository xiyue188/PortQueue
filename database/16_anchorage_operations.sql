/* 4C 增量：不回填、不重解释旧预计；已存在业务数据只得到空人工预计与版本0。
   已有库按 16 -> 04 -> 09 部署。全新库01已带新列，仍需本脚本创建预计过程。 */
USE [PortQueue];
GO
SET XACT_ABORT ON;
-- 普通 sqlcmd 不默认启用过滤索引所需选项，迁移必须自行声明。
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.anchorage_queue_entry','manual_estimated_berthing_at') IS NULL
    ALTER TABLE dbo.anchorage_queue_entry ADD manual_estimated_berthing_at datetime2(0) NULL;
IF COL_LENGTH('dbo.anchorage_queue_entry','estimate_version') IS NULL
BEGIN
    ALTER TABLE dbo.anchorage_queue_entry ADD estimate_version bigint NOT NULL
        CONSTRAINT DF_queue_estimate_version DEFAULT(0) WITH VALUES;
    -- 新列须在DDL执行后再绑定约束；同批静态编译会仍按旧表结构解析。
    EXEC(N'ALTER TABLE dbo.anchorage_queue_entry ADD CONSTRAINT CK_queue_estimate_version CHECK(estimate_version>=0);');
END;
COMMIT TRANSACTION;
GO
CREATE OR ALTER PROCEDURE dbo.usp_set_anchorage_estimate
    @queue_entry_id bigint,
    @estimated_at datetime2(7),
    @expected_version bigint,
    @reason nvarchar(max),
    @operator_id bigint,
    @home_port_code varchar(10)='SH-YS'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @lock int;
        EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @lock<0 THROW 52211,'Schedule is busy.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.user_account u WITH(UPDLOCK,HOLDLOCK) JOIN dbo.role r WITH(HOLDLOCK) ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED' AND u.role_code IN ('ADMIN','DISPATCHER'))
            THROW 52201,'Estimate requires an enabled administrator or dispatcher.',1;
        SET @reason=LTRIM(RTRIM(@reason));
        IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),N''),CHAR(10),N''),CHAR(13),N''),N'　',N''))),N'') IS NULL OR DATALENGTH(@reason)>1000
            THROW 52209,'A reason of at most 500 characters is required.',1;
        DECLARE @version bigint,@entered datetime2(0),@status varchar(30),@left datetime2(0),@appointment_status varchar(30);
        SELECT @version=raw.estimate_version,@entered=q.entered_anchorage_at,@left=q.left_anchorage_at,
            @status=q.queue_status_code,@appointment_status=aa.appointment_status_code
        FROM dbo.anchorage_queue_entry raw JOIN dbo.v_anchorage_queue q ON q.queue_entry_id=raw.queue_entry_id
        JOIN dbo.anchorage a ON a.anchorage_id=q.anchorage_id JOIN dbo.port p ON p.port_id=a.port_id
        JOIN dbo.arrival_appointment aa ON aa.appointment_id=q.appointment_id
        WHERE q.queue_entry_id=@queue_entry_id AND p.port_code=@home_port_code;
        IF @version IS NULL THROW 52203,'Anchorage stay does not exist in the home port.',1;
        IF @status NOT IN ('WAITING','ASSIGNED') OR @entered IS NULL OR @left IS NOT NULL OR @appointment_status<>'APPROVED'
            THROW 52205,'Only a current active stay can have a manual estimate.',1;
        IF @expected_version IS NULL OR @expected_version<>@version THROW 52204,'Estimate version has changed.',1;
        IF @estimated_at IS NOT NULL AND (@estimated_at<SYSUTCDATETIME() OR @estimated_at<@entered
            OR DATEPART(nanosecond,@estimated_at)<>0 OR YEAR(@estimated_at) NOT BETWEEN 1900 AND 9998)
            THROW 52210,'Estimate must be a future whole-second time.',1;
        IF @version=9223372036854775807 THROW 52213,'Estimate version is exhausted.',1;
        DECLARE @before nvarchar(max)=(SELECT * FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@queue_entry_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.anchorage_queue_entry SET manual_estimated_berthing_at=@estimated_at,estimate_version=estimate_version+1 WHERE queue_entry_id=@queue_entry_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('anchorage_queue_entry',CONVERT(varchar(100),@queue_entry_id),'UPDATE',@before,
            (SELECT q.*,@reason AS operation_reason FROM dbo.anchorage_queue_entry q WHERE queue_entry_id=@queue_entry_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@operator_id);
        COMMIT TRANSACTION;
        SELECT CONVERT(varchar(20),@queue_entry_id) AS QueueEntryId,
            TODATETIMEOFFSET(CONVERT(datetime2(0),@estimated_at),'+00:00') AS ManualEstimatedBerthingAt,
            CONVERT(varchar(20),@version+1) AS EstimateVersion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
