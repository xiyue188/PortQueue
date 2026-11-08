/*
   模块：可重复的教学演示数据，独立于参考种子。需管理员主动执行，不随启动自动装载。
   时间：固定 UTC 2026-11-01（页面按 Asia/Shanghai 展示）；均为合成资料，不是真实船舶航迹。
   历史已审批预约与已确认计划作为历史快照导入，保留原确认时间；事件仍通过业务过程生成。
   理由：不能把今天确认的计划伪装成过去靠泊时的执行依据。正常创建/审核/确认另由测试覆盖。
   重跑按 PQ-DEMO-* 业务编号跳过已有记录，不清空、不重置已推进状态，不重复追加审计。
*/
USE [PortQueue];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- 独立执行时显式满足过滤唯一索引的 SET 选项，不依赖 sqlcmd -I 或编辑器默认值。
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF @lock<0 THROW 53200,'Demo data is busy.',1;
    DECLARE @u bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher' AND status_code='ENABLED');
    DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='CNTAO');
    DECLARE @company bigint=(SELECT company_id FROM dbo.shipping_company WHERE company_code='DEMO-SC');
    DECLARE @type bigint=(SELECT dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type='SHIP_TYPE' AND item_code='CONTAINER' AND is_enabled=1);
    IF @u IS NULL OR @port IS NULL OR @company IS NULL OR @type IS NULL THROW 53201,'Reference data is missing or disabled.',1;
    IF SYSUTCDATETIME()<'2026-11-02' THROW 53202,'Demo scenario cannot be loaded before its event dates.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.terminal WHERE port_id=@port AND terminal_code='PQ-DEMO-T')
        INSERT dbo.terminal(port_id,terminal_code,terminal_name,status_code) VALUES(@port,'PQ-DEMO-T',N'Demo Terminal','AVAILABLE');
    DECLARE @t bigint=(SELECT terminal_id FROM dbo.terminal WHERE port_id=@port AND terminal_code='PQ-DEMO-T');
    IF NOT EXISTS(SELECT 1 FROM dbo.berth WHERE terminal_id=@t AND berth_code='PQ-DEMO-B')
        INSERT dbo.berth(terminal_id,berth_code,berth_name,length_m,max_draft_m,max_dwt,status_code)
        VALUES(@t,'PQ-DEMO-B',N'Demo Berth',350,15,100000,'AVAILABLE');
    DECLARE @b bigint=(SELECT berth_id FROM dbo.berth WHERE terminal_id=@t AND berth_code='PQ-DEMO-B');
    IF NOT EXISTS(SELECT 1 FROM dbo.berth_ship_type WHERE berth_id=@b AND ship_type_item_id=@type)
        INSERT dbo.berth_ship_type VALUES(@b,@type,'ENABLED');
    IF NOT EXISTS(SELECT 1 FROM dbo.anchorage WHERE port_id=@port AND anchorage_code='PQ-DEMO-A')
        INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
        VALUES(@port,'PQ-DEMO-A',N'Demo Anchorage','OUTER',20,'AVAILABLE');
    DECLARE @a bigint=(SELECT anchorage_id FROM dbo.anchorage WHERE port_id=@port AND anchorage_code='PQ-DEMO-A');
    DECLARE @n int=1,@s bigint,@c bigint,@ap bigint,@p bigint,@r bigint,@start datetime2(0),@finish datetime2(0),@actual datetime2(0);
    WHILE @n<=5
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no IN
            (CONCAT('PQ-DEMO-00',@n),CONCAT('PQ20261101-000',@n)))
        BEGIN
            SET @s=NULL;
            SELECT @s=ship_id FROM dbo.ship WHERE imo_no=CONCAT('PQDEMO',@n);
            IF @s IS NULL
            BEGIN
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
                VALUES(CONCAT('PQDEMO',@n),CONCAT('99001000',@n),CONCAT(N'Demo Vessel ',@n),@type,180,25,10,30000,@company,'ACTIVE');
                SET @s=SCOPE_IDENTITY();
            END;
            -- 示例船舶标识不代表真实 IMO 注册；使用稳定编号便于识别和隔离。
            INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
            VALUES(CONCAT('PQ-DEMO-CALL-',@n),@s,'2026-11-01','2026-11-03','PLANNED');
            SET @c=SCOPE_IDENTITY();
            INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,
                estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at,review_comment)
            VALUES(CONCAT('PQ-DEMO-00',@n),@c,'2026-11-01','2026-11-03',2,9,3,
                CASE WHEN @n=5 THEN 'PENDING' ELSE 'APPROVED' END,'2026-10-30',
                CASE WHEN @n=5 THEN NULL ELSE @u END,CASE WHEN @n=5 THEN NULL ELSE '2026-10-31' END,
                CASE WHEN @n=5 THEN NULL ELSE N'Historical demonstration approval' END);
            SET @ap=SCOPE_IDENTITY();
            INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
            VALUES('arrival_appointment',CONVERT(varchar(100),@ap),'INSERT',
                (SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@ap FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
            INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
            VALUES('port_call',CONVERT(varchar(100),@c),'INSERT',
                (SELECT * FROM dbo.port_call WHERE port_call_id=@c FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
            IF @n<=2
            BEGIN
                SET @start=CASE WHEN @n=1 THEN '2026-11-01T02:00:00' ELSE '2026-11-01T06:00:00' END;
                SET @finish=DATEADD(hour,2,@start);
                INSERT dbo.berth_plan(appointment_id,plan_status_code,created_at,updated_at)
                VALUES(@ap,'CONFIRMED','2026-10-31','2026-10-31');
                SET @p=SCOPE_IDENTITY();
                INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,
                    revision_reason,revised_by,revised_at,confirmed_by,confirmed_at,is_current)
                VALUES(@p,1,@b,@start,@finish,N'Historical demonstration plan',@u,'2026-10-31',@u,'2026-10-31',1);
                SET @r=SCOPE_IDENTITY();
                INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
                VALUES('berth_plan',CONVERT(varchar(100),@p),'INSERT',
                    (SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@p FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
                INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
                VALUES('berth_plan_revision',CONVERT(varchar(100),@r),'INSERT',
                    (SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@r FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
                IF @n=1
                BEGIN
                    EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_ENTER','2026-11-01T00:00:00',@u;
                    EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_LEAVE','2026-11-01T01:30:00',@u;
                END;
                SET @actual=DATEADD(minute,-20,@start);
                EXEC dbo.usp_record_port_entry @ap,@actual,@u;
                SET @actual=DATEADD(minute,CASE WHEN @n=1 THEN 0 ELSE 10 END,@start);
                EXEC dbo.usp_record_berth_event @ap,'BERTHING',@actual,@r,@u;
                SET @actual=DATEADD(minute,CASE WHEN @n=1 THEN 0 ELSE 20 END,@finish);
                EXEC dbo.usp_record_berth_event @ap,'UNBERTHING',@actual,@r,@u;
            END;
            IF @n IN (3,4)
            BEGIN
                SET @actual=DATEADD(minute,@n*10,CONVERT(datetime2(0),'2026-11-01T09:00:00'));
                EXEC dbo.usp_record_anchorage_event @ap,@a,'ANCHORAGE_ENTER',@actual,@u;
            END;
        END;
        SET @n+=1;
    END;
    COMMIT TRANSACTION;
    SELECT appointment_no,appointment_status_code FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ-DEMO-%' ORDER BY appointment_no;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
