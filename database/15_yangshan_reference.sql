/*
   中期主港场景调整：上海洋山深水港，SH-YS 为项目内部代码。
   新库在 05 之后执行；旧青岛场景库须先执行本文件，再重跑更新后的 05。
   旧青岛港保留为外部港口参考；只迁移已知合成设施，
   不更改航次前后港、预约状态、事件或旧审计。新旧资料同事务留痕，可重跑。
   未采集洋山锚地坐标，迁移时置空，不复制青岛坐标或编造真实地理位置。
*/
USE [PortQueue];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int, @actor bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
    EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF @lock<0 OR @actor IS NULL THROW 53400,'Reference operator is missing or schedule is busy.',1;
    DECLARE @old bigint=(SELECT port_id FROM dbo.port WHERE port_code='CNTAO'),
        @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS');
    IF @port IS NULL
    BEGIN
        INSERT dbo.port(port_code,port_name,country_code,timezone,status_code)
        VALUES('SH-YS',N'上海洋山深水港','CN','Asia/Shanghai','ENABLED');
        SET @port=SCOPE_IDENTITY();
        INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
        VALUES('port',CONVERT(varchar(100),@port),'INSERT',
            (SELECT * FROM dbo.port WHERE port_id=@port FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor);
    END;
    IF NOT EXISTS(SELECT 1 FROM dbo.port WHERE port_id=@port AND port_name=N'上海洋山深水港' AND country_code='CN' AND status_code='ENABLED')
        THROW 53401,'SH-YS is already used by different reference data; no data changed.',1;

    -- 在迁移前核对目标候选键，避免覆盖人工维护的同编码设施。
    IF EXISTS(SELECT 1 FROM dbo.terminal a JOIN dbo.terminal b ON a.terminal_code=b.terminal_code
        WHERE a.port_id=@old AND b.port_id=@port AND a.terminal_code IN('T-A','PQ-DEMO-T','PQ-CT'))
        OR EXISTS(SELECT 1 FROM dbo.anchorage a JOIN dbo.anchorage b ON a.anchorage_code=b.anchorage_code
        WHERE a.port_id=@old AND b.port_id=@port AND a.anchorage_code IN('ANCH-OUTER-A','ANCH-INNER-A','PQ-DEMO-A','PQ-OUTER'))
        THROW 53402,'Target facilities conflict; inspect references before migration.',1;

    DECLARE @terminals TABLE(id bigint PRIMARY KEY,old_json nvarchar(max));
    INSERT @terminals SELECT terminal_id,(SELECT t.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)
    FROM dbo.terminal t WHERE port_id=@old AND
        ((terminal_code='T-A' AND terminal_name=N'Container Terminal A')
        OR (terminal_code='PQ-DEMO-T' AND terminal_name IN(N'Demo Terminal',N'综合作业码头'))
        OR (terminal_code='PQ-CT' AND terminal_name=N'前湾集装箱作业区'));
    UPDATE t SET port_id=@port,terminal_name=CASE terminal_code
        WHEN 'T-A' THEN N'集装箱作业区 A' WHEN 'PQ-CT' THEN N'集装箱作业区 B' ELSE N'综合作业码头' END
    FROM dbo.terminal t JOIN @terminals m ON m.id=t.terminal_id;
    INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
    SELECT 'terminal',CONVERT(varchar(100),m.id),'UPDATE',m.old_json,
        (SELECT t.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor
    FROM @terminals m JOIN dbo.terminal t ON t.terminal_id=m.id;

    DECLARE @anchorages TABLE(id bigint PRIMARY KEY,old_json nvarchar(max));
    INSERT @anchorages SELECT anchorage_id,(SELECT a.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)
    FROM dbo.anchorage a WHERE port_id=@old AND
        ((anchorage_code='ANCH-OUTER-A' AND anchorage_name=N'Outer Anchorage A')
        OR (anchorage_code='ANCH-INNER-A' AND anchorage_name=N'Inner Anchorage A')
        OR (anchorage_code='PQ-DEMO-A' AND anchorage_name IN(N'Demo Anchorage',N'港外待泊锚地'))
        OR (anchorage_code='PQ-OUTER' AND anchorage_name=N'外港待泊锚地'));
    UPDATE a SET port_id=@port,longitude=NULL,latitude=NULL,anchorage_name=CASE anchorage_code
        WHEN 'ANCH-OUTER-A' THEN N'外侧待泊锚地 A' WHEN 'ANCH-INNER-A' THEN N'内侧待泊锚地 A' ELSE anchorage_name END
    FROM dbo.anchorage a JOIN @anchorages m ON m.id=a.anchorage_id;
    INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
    SELECT 'anchorage',CONVERT(varchar(100),m.id),'UPDATE',m.old_json,
        (SELECT a.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor
    FROM @anchorages m JOIN dbo.anchorage a ON a.anchorage_id=m.id;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
