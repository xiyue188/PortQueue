/*
   一次性样例名称整理，可重跑。仅匹配旧种子明确标识及原始名称，保留主键和业务事实。
   不更改事件、审核时间、状态或历史审计；每次实际改动追加前后快照。
   这些资料仍是合成教学场景，界面使用正式业务名称，不表示真实注册船舶。
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
    DECLARE @lock int,@u bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
    EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF @lock<0 OR @u IS NULL THROW 53300,'Sample prerequisites are missing or busy.',1;
    DECLARE @n int=1,@id bigint,@before nvarchar(max),@name nvarchar(100),@imo varchar(7),@digits varchar(6);
    WHILE @n<=5
    BEGIN
        SET @id=NULL;
        SELECT @id=appointment_id FROM dbo.arrival_appointment WHERE appointment_no=CONCAT('PQ-DEMO-00',@n);
        IF @id IS NOT NULL
        BEGIN
            SET @before=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.arrival_appointment SET appointment_no=CONCAT('PQ20261101-000',@n),
                review_comment=CASE WHEN review_comment=N'Historical demonstration approval' THEN N'船舶资料与申报吃水核验通过。' ELSE review_comment END
            WHERE appointment_id=@id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
            VALUES('arrival_appointment',CONVERT(varchar(100),@id),'UPDATE',@before,
                (SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
        END;
        SET @id=NULL;
        SELECT @id=ship_id FROM dbo.ship WHERE imo_no=CONCAT('PQDEMO',@n) AND ship_name=CONCAT(N'Demo Vessel ',@n);
        IF @id IS NOT NULL
        BEGIN
            SET @name=CASE @n WHEN 1 THEN N'海岚远航' WHEN 2 THEN N'东泽明珠' WHEN 3 THEN N'新澜先锋' WHEN 4 THEN N'海岚星河' ELSE N'东泽启航' END;
            SET @before=(SELECT * FROM dbo.ship WHERE ship_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            -- 七位格式及校验位正确的合成编号，不声称对应真实 IMO 登记。
            SET @digits=CONVERT(varchar(6),980000+@n);
            SET @imo=@digits+CONVERT(varchar(1),(7*CONVERT(int,SUBSTRING(@digits,1,1))+6*CONVERT(int,SUBSTRING(@digits,2,1))+5*CONVERT(int,SUBSTRING(@digits,3,1))+4*CONVERT(int,SUBSTRING(@digits,4,1))+3*CONVERT(int,SUBSTRING(@digits,5,1))+2*CONVERT(int,SUBSTRING(@digits,6,1)))%10);
            UPDATE dbo.ship SET ship_name=@name,imo_no=@imo WHERE ship_id=@id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
            VALUES('ship',CONVERT(varchar(100),@id),'UPDATE',@before,
                (SELECT * FROM dbo.ship WHERE ship_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
        END;
        SET @n+=1;
    END;
    IF EXISTS(SELECT 1 FROM dbo.user_account WHERE user_id=@u AND display_name=N'Demo Dispatcher')
    BEGIN
        SET @before=(SELECT user_id,display_name,role_code FROM dbo.user_account WHERE user_id=@u FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.user_account SET display_name=N'陈舟' WHERE user_id=@u;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('user_account',CONVERT(varchar(100),@u),'UPDATE',@before,
            (SELECT user_id,display_name,role_code FROM dbo.user_account WHERE user_id=@u FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    -- 新查询页复用旧设施：只规范明确匹配的初始显示名称，不重解释代码。
    SET @id=NULL;
    SELECT @id=port_id FROM dbo.port WHERE port_code='CNTAO' AND port_name=N'Qingdao Port';
    IF @id IS NOT NULL
    BEGIN
        SET @before=(SELECT * FROM dbo.port WHERE port_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.port SET port_name=N'青岛港' WHERE port_id=@id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('port',CONVERT(varchar(100),@id),'UPDATE',@before,
            (SELECT * FROM dbo.port WHERE port_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    SET @id=NULL;
    SELECT @id=company_id FROM dbo.shipping_company WHERE company_code='DEMO-SC' AND company_name=N'Demo Ocean Shipping';
    IF @id IS NOT NULL
    BEGIN
        SET @before=(SELECT * FROM dbo.shipping_company WHERE company_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.shipping_company SET company_name=N'海岚航运有限公司' WHERE company_id=@id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('shipping_company',CONVERT(varchar(100),@id),'UPDATE',@before,
            (SELECT * FROM dbo.shipping_company WHERE company_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    SET @id=NULL;
    SELECT @id=terminal_id FROM dbo.terminal WHERE terminal_code='PQ-DEMO-T' AND terminal_name=N'Demo Terminal';
    IF @id IS NOT NULL
    BEGIN
        SET @before=(SELECT * FROM dbo.terminal WHERE terminal_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.terminal SET terminal_name=N'综合作业码头' WHERE terminal_id=@id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('terminal',CONVERT(varchar(100),@id),'UPDATE',@before,
            (SELECT * FROM dbo.terminal WHERE terminal_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    SET @id=NULL;
    SELECT @id=berth_id FROM dbo.berth WHERE berth_code='PQ-DEMO-B' AND berth_name=N'Demo Berth';
    IF @id IS NOT NULL
    BEGIN
        SET @before=(SELECT * FROM dbo.berth WHERE berth_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.berth SET berth_name=N'综合作业泊位' WHERE berth_id=@id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('berth',CONVERT(varchar(100),@id),'UPDATE',@before,
            (SELECT * FROM dbo.berth WHERE berth_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    SET @id=NULL;
    SELECT @id=anchorage_id FROM dbo.anchorage WHERE anchorage_code='PQ-DEMO-A' AND anchorage_name=N'Demo Anchorage';
    IF @id IS NOT NULL
    BEGIN
        SET @before=(SELECT * FROM dbo.anchorage WHERE anchorage_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.anchorage SET anchorage_name=N'港外待泊锚地' WHERE anchorage_id=@id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('anchorage',CONVERT(varchar(100),@id),'UPDATE',@before,
            (SELECT * FROM dbo.anchorage WHERE anchorage_id=@id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@u);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
