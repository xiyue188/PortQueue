/*
   模块：48 条可重复装载的港口业务场景。编号 PQ202611-1001..1048 是本批保留空间。
   合成船名、IMO/MMSI、人员与历史资料仅用于课程验证，不代表实时港口或真实注册信息。
   执行前完成 00—05；12 是旧五条样例的可选名称整理。全批事务，重跑跳过已存在的预约。
   历史审核/确认作为带原时间的快照导入；实际进出锚、进港、靠离泊通过既有事务过程生成。
   新的确认计划走创建/确认过程；草稿、撤销为导入场景，不暗示对应写 API 已实现。
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
    DECLARE @lock int;
    EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF @lock<0 THROW 53310,'Scenario import is busy.',1;
    IF SYSUTCDATETIME()<'2026-11-07' THROW 53311,'Scenario event dates are in the future.',1;
    DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='CNTAO'),
        @type bigint=(SELECT dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type='SHIP_TYPE' AND item_code='CONTAINER' AND is_enabled=1);
    IF @port IS NULL OR @type IS NULL THROW 53312,'Port or enabled ship type is missing.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.user_account WHERE username='scenario.reviewer')
        INSERT dbo.user_account(username,display_name,role_code,password_hash,status_code)
        VALUES('scenario.reviewer',N'林悦','REVIEWER',CRYPT_GEN_RANDOM(32),'ENABLED');
    IF NOT EXISTS(SELECT 1 FROM dbo.user_account WHERE username='scenario.dispatcher')
        INSERT dbo.user_account(username,display_name,role_code,password_hash,status_code)
        VALUES('scenario.dispatcher',N'周航','DISPATCHER',CRYPT_GEN_RANDOM(32),'ENABLED');
    DECLARE @u bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario.dispatcher' AND status_code='ENABLED'),
        @reviewer bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario.reviewer' AND status_code='ENABLED');
    IF @u IS NULL OR @reviewer IS NULL THROW 53313,'Scenario operators are disabled.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.shipping_company WHERE company_code='PQ-HL')
        INSERT dbo.shipping_company(company_code,company_name,status_code) VALUES('PQ-HL',N'海岚航运有限公司','ACTIVE');
    DECLARE @company bigint=(SELECT company_id FROM dbo.shipping_company WHERE company_code='PQ-HL');
    IF NOT EXISTS(SELECT 1 FROM dbo.terminal WHERE port_id=@port AND terminal_code='PQ-CT')
        INSERT dbo.terminal(port_id,terminal_code,terminal_name,status_code) VALUES(@port,'PQ-CT',N'前湾集装箱作业区','AVAILABLE');
    DECLARE @terminal bigint=(SELECT terminal_id FROM dbo.terminal WHERE port_id=@port AND terminal_code='PQ-CT');
    IF NOT EXISTS(SELECT 1 FROM dbo.berth WHERE terminal_id=@terminal AND berth_code='PQ-C01')
        INSERT dbo.berth(terminal_id,berth_code,berth_name,length_m,max_draft_m,max_dwt,status_code)
        VALUES(@terminal,'PQ-C01',N'集装箱 01 号泊位',380,16,150000,'AVAILABLE');
    DECLARE @berth bigint=(SELECT berth_id FROM dbo.berth WHERE terminal_id=@terminal AND berth_code='PQ-C01');
    IF NOT EXISTS(SELECT 1 FROM dbo.berth_ship_type WHERE berth_id=@berth AND ship_type_item_id=@type)
        INSERT dbo.berth_ship_type VALUES(@berth,@type,'ENABLED');
    IF NOT EXISTS(SELECT 1 FROM dbo.anchorage WHERE port_id=@port AND anchorage_code='PQ-OUTER')
        INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
        VALUES(@port,'PQ-OUTER',N'外港待泊锚地','OUTER',30,'AVAILABLE');
    DECLARE @anchorage bigint=(SELECT anchorage_id FROM dbo.anchorage WHERE port_id=@port AND anchorage_code='PQ-OUTER');

    DECLARE @n int=1,@group int,@kind int,@no varchar(40),@digits varchar(6),@imo varchar(7),@ship bigint,@call bigint,@ap bigint,@plan bigint,@revision bigint,
        @arrival datetime2(0),@departure datetime2(0),@reviewed datetime2(0),@start datetime2(0),@finish datetime2(0),@event datetime2(0),@status varchar(30),@name nvarchar(100);
    DECLARE @created TABLE(berth_plan_id bigint,revision_id bigint,version_no int);
    WHILE @n<=48
    BEGIN
        SET @no=CONCAT('PQ202611-',1000+@n);
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no=@no)
        BEGIN
            SET @group=(@n-1)/6; SET @kind=(@n-1)%6;
            SET @status=CASE @kind WHEN 0 THEN 'DRAFT' WHEN 1 THEN 'PENDING' WHEN 2 THEN 'APPROVED' WHEN 3 THEN 'REJECTED' WHEN 4 THEN 'CANCELLED' ELSE 'APPROVED' END;
            SET @arrival=CASE WHEN @kind=5 THEN DATEADD(hour,@group*24+4,CONVERT(datetime2(0),'2026-10-27'))
                ELSE DATEADD(minute,@group*190+@kind*45,CONVERT(datetime2(0),'2026-11-08T00:00:00')) END;
            SET @departure=DATEADD(hour,14+@group%4*4,@arrival);
            IF @kind=2
            BEGIN
                SET @arrival=DATEADD(minute,@group*35,CONVERT(datetime2(0),'2026-11-06T02:00:00'));
                SET @departure=DATEADD(hour,@group*12+12,CONVERT(datetime2(0),'2026-11-09'));
            END;
            SET @reviewed=CASE WHEN @kind=5 THEN DATEADD(day,-1,@arrival) ELSE CONVERT(datetime2(0),'2026-11-05T02:00:00') END;
            SET @name=CONCAT(CASE @group%4 WHEN 0 THEN N'海岚' WHEN 1 THEN N'东泽' WHEN 2 THEN N'新澜' ELSE N'远川' END,
                CASE @kind WHEN 0 THEN N'远航' WHEN 1 THEN N'明珠' WHEN 2 THEN N'星河' WHEN 3 THEN N'启航' WHEN 4 THEN N'长风' ELSE N'海岳' END,N' ',CONVERT(nvarchar(2),@group+1));
            SET @digits=CONVERT(varchar(6),980100+@n);
            SET @imo=@digits+CONVERT(varchar(1),(7*CONVERT(int,SUBSTRING(@digits,1,1))+6*CONVERT(int,SUBSTRING(@digits,2,1))+5*CONVERT(int,SUBSTRING(@digits,3,1))+4*CONVERT(int,SUBSTRING(@digits,4,1))+3*CONVERT(int,SUBSTRING(@digits,5,1))+2*CONVERT(int,SUBSTRING(@digits,6,1)))%10);
            INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
            VALUES(@imo,CONCAT('99002',RIGHT(CONCAT('0000',@n),4)),@name,@type,160+@group*12,25+@group,11,30000+@group*6000,@company,'ACTIVE');
            SET @ship=SCOPE_IDENTITY();
            INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,cargo_teu,call_status_code)
            VALUES(CONCAT('PC202611-',1000+@n),@ship,@arrival,@departure,320+@n*24,CASE WHEN @kind=4 THEN 'CANCELLED' ELSE 'PLANNED' END);
            SET @call=SCOPE_IDENTITY();
            INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,
                declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at,review_comment)
            VALUES(@no,@call,@arrival,@departure,6+@group%4*2,9+@group%3*.5,2+@group%3,@status,
                CASE WHEN @kind=0 THEN NULL ELSE DATEADD(day,-1,@reviewed) END,
                CASE WHEN @kind IN (2,3,5) THEN @reviewer ELSE NULL END,CASE WHEN @kind IN (2,3,5) THEN @reviewed ELSE NULL END,
                CASE WHEN @kind=3 THEN N'申报资料不完整，需补充装载清单后重新申请。' WHEN @kind IN (2,5) THEN N'船舶资料及申报吃水已核验。' ELSE NULL END);
            SET @ap=SCOPE_IDENTITY();
            INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
            VALUES('arrival_appointment',CONVERT(varchar(100),@ap),'INSERT',(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@ap FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u),
                ('port_call',CONVERT(varchar(100),@call),'INSERT',(SELECT * FROM dbo.port_call WHERE port_call_id=@call FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
            IF @kind=2
            BEGIN
                SET @event=DATEADD(minute,@group*35,CONVERT(datetime2(0),'2026-11-06T02:00:00'));
                EXEC dbo.usp_record_anchorage_event @ap,@anchorage,'ANCHORAGE_ENTER',@event,@u;
                IF @group%2=0
                BEGIN
                    SET @start=DATEADD(hour,@group*12,CONVERT(datetime2(0),'2026-11-09'));
                    SET @finish=DATEADD(hour,8,@start);
                    DELETE FROM @created;
                    INSERT @created EXEC dbo.usp_create_berth_plan_revision @ap,@berth,@start,@finish,N'集装箱船靠泊安排',@u;
                    SELECT @plan=berth_plan_id,@revision=revision_id FROM @created;
                    EXEC dbo.usp_confirm_berth_plan @plan,@revision,@u;
                END;
            END;
            IF @kind=5
            BEGIN
                SET @start=DATEADD(hour,1,@arrival); SET @finish=DATEADD(hour,8,@start);
                INSERT dbo.berth_plan(appointment_id,plan_status_code,created_at,updated_at) VALUES(@ap,'CONFIRMED',@reviewed,@reviewed);
                SET @plan=SCOPE_IDENTITY();
                INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,planned_unberthing_at,revision_reason,revised_by,revised_at,confirmed_by,confirmed_at,is_current)
                VALUES(@plan,1,@berth,@start,@finish,N'已执行航次计划归档',@u,@reviewed,@u,@reviewed,1);
                SET @revision=SCOPE_IDENTITY();
                INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
                VALUES('berth_plan',CONVERT(varchar(100),@plan),'INSERT',(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@plan FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@u),
                    ('berth_plan_revision',CONVERT(varchar(100),@revision),'INSERT',(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@revision FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@u);
                EXEC dbo.usp_record_port_entry @ap,@arrival,@u;
                EXEC dbo.usp_record_berth_event @ap,'BERTHING',@start,@revision,@u;
                SET @event=DATEADD(minute,@group*7,@finish);
                EXEC dbo.usp_record_berth_event @ap,'UNBERTHING',@event,@revision,@u;
            END;
        END;
        SET @n+=1;
    END;
    COMMIT TRANSACTION;
    SELECT appointment_status_code,COUNT(*) AS records FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ202611-10%' GROUP BY appointment_status_code;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
