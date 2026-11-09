/* 阶段3C增量部署：登记/草稿编辑/提交共用受控事务。无需重建基本表。
   request_key只用于创建幂等；审计序号用于草稿并发检查，不增加重复业务事实。 */
USE [PortQueue];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_save_appointment
    @action varchar(10), @operator_id bigint, @payload nvarchar(max)=NULL,
    @request_key uniqueidentifier=NULL, @appointment_id bigint=NULL,
    @expected_version bigint=NULL, @submit bit=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @lock_result int;
        EXEC @lock_result=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @lock_result<0 THROW 52109,'Schedule busy.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.user_account u WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.role r WITH(HOLDLOCK) ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED' AND u.role_code IN('ADMIN','DISPATCHER'))
            THROW 52103,'Registration permission required.',1;
        IF @action IS NULL OR @action NOT IN('CREATE','EDIT','SUBMIT') OR @submit IS NULL OR (@action='EDIT' AND @submit=1)
            THROW 52100,'Invalid command.',1;

        DECLARE @call_id bigint,@status varchar(30),@version bigint,@no varchar(40),@hash varchar(64),
            @old_appointment nvarchar(max),@old_call nvarchar(max),@replayed bit=0;
        IF @action='CREATE'
        BEGIN
            IF @request_key IS NULL OR @request_key='00000000-0000-0000-0000-000000000000' OR ISJSON(@payload)<>1 OR @payload IS NULL
                THROW 52100,'Creation key and payload required.',1;
            SET @appointment_id=NULL;
            SET @no='PQ'+REPLACE(CONVERT(varchar(36),@request_key),'-','');
            SET @hash=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@payload,'|',@submit)),2);
            SELECT @appointment_id=appointment_id,@status=appointment_status_code,@call_id=port_call_id
                FROM dbo.arrival_appointment WITH(UPDLOCK,HOLDLOCK) WHERE appointment_no=@no;
            IF @appointment_id IS NOT NULL
            BEGIN
                IF NOT EXISTS(SELECT 1 FROM dbo.audit_log WHERE table_name='arrival_appointment'
                    AND record_id=CONVERT(varchar(100),@appointment_id) AND operation_type='INSERT'
                    AND operator_id=@operator_id AND JSON_VALUE(new_data_json,'$.request_hash')=@hash)
                    THROW 52104,'Creation key already used with different content or actor.',1;
                SET @replayed=1;
            END;
        END
        ELSE
        BEGIN
            SELECT @call_id=port_call_id,@status=appointment_status_code FROM dbo.arrival_appointment WITH(UPDLOCK,HOLDLOCK)
                WHERE appointment_id=@appointment_id;
            IF @call_id IS NULL THROW 52101,'Appointment not found.',1;
            IF @status<>'DRAFT' THROW 52102,'Only drafts can be changed or submitted.',1;
            SELECT @version=COALESCE(MAX(audit_id),0) FROM dbo.audit_log WHERE
                (table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@appointment_id)) OR
                (table_name='port_call' AND record_id=CONVERT(varchar(100),@call_id));
            IF @expected_version IS NULL OR @expected_version<>@version THROW 52105,'Draft changed. Reload before saving.',1;
            SET @old_appointment=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            SET @old_call=(SELECT * FROM dbo.port_call WITH(UPDLOCK,HOLDLOCK) WHERE port_call_id=@call_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            IF EXISTS(SELECT 1 FROM dbo.port_call WHERE port_call_id=@call_id AND call_status_code<>'PLANNED')
                THROW 52102,'Port call is no longer planned.',1;
        END;

        IF @replayed=0
        BEGIN
            -- SUBMIT重新读取事实，而非接受客户端重传旧表单覆盖草稿。
            IF @action='SUBMIT'
                SET @payload=(SELECT CONVERT(varchar(20),pc.ship_id) shipId,CONVERT(varchar(20),pc.previous_port_id) previousPortId,
                    CONVERT(varchar(20),pc.next_port_id) nextPortId,TODATETIMEOFFSET(pc.planned_arrival_at,'+00:00') plannedArrivalAt,
                    TODATETIMEOFFSET(pc.planned_departure_at,'+00:00') plannedDepartureAt,pc.cargo_teu cargoTeu,pc.cargo_weight_t cargoWeightT,
                    TODATETIMEOFFSET(a.requested_arrival_at,'+00:00') requestedArrivalAt,TODATETIMEOFFSET(a.requested_departure_at,'+00:00') requestedDepartureAt,
                    a.estimated_operation_hours estimatedOperationHours,a.declared_draft_m declaredDraftM,a.priority_level priorityLevel,
                    a.dangerous_goods_flag dangerousGoodsFlag,a.special_requirement specialRequirement
                    FROM dbo.arrival_appointment a JOIN dbo.port_call pc ON pc.port_call_id=a.port_call_id WHERE a.appointment_id=@appointment_id
                    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            IF @payload IS NULL OR ISJSON(@payload)<>1 THROW 52100,'Invalid payload.',1;
            DECLARE @ship bigint=TRY_CONVERT(bigint,JSON_VALUE(@payload,'$.shipId')),
                @previous bigint=TRY_CONVERT(bigint,JSON_VALUE(@payload,'$.previousPortId')),
                @next bigint=TRY_CONVERT(bigint,JSON_VALUE(@payload,'$.nextPortId')),
                @arrival datetimeoffset(7)=TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@payload,'$.requestedArrivalAt')),
                @departure datetimeoffset(7)=TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@payload,'$.requestedDepartureAt')),
                @planned_arrival datetimeoffset(7)=TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@payload,'$.plannedArrivalAt')),
                @planned_departure datetimeoffset(7)=TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@payload,'$.plannedDepartureAt')),
                @hours decimal(38,18)=TRY_CONVERT(decimal(38,18),JSON_VALUE(@payload,'$.estimatedOperationHours')),
                @draft decimal(38,18)=TRY_CONVERT(decimal(38,18),JSON_VALUE(@payload,'$.declaredDraftM')),
                @weight decimal(38,18)=TRY_CONVERT(decimal(38,18),JSON_VALUE(@payload,'$.cargoWeightT')),
                @teu int=TRY_CONVERT(int,JSON_VALUE(@payload,'$.cargoTeu')),
                @priority int=TRY_CONVERT(int,JSON_VALUE(@payload,'$.priorityLevel')),
                @danger bit=CASE JSON_VALUE(@payload,'$.dangerousGoodsFlag') WHEN 'true' THEN 1 WHEN 'false' THEN 0 END,
                @requirement nvarchar(max);
            SELECT @requirement=specialRequirement FROM OPENJSON(@payload) WITH(specialRequirement nvarchar(max) '$.specialRequirement');
            IF @ship IS NULL OR @arrival IS NULL OR @departure IS NULL OR @planned_arrival IS NULL
                OR @departure<=@arrival OR @planned_departure<=@planned_arrival
                OR DATEPART(NANOSECOND,@arrival)<>0 OR DATEPART(NANOSECOND,@departure)<>0
                OR DATEPART(NANOSECOND,@planned_arrival)<>0 OR DATEPART(NANOSECOND,@planned_departure)<>0
                OR @hours IS NULL OR @hours<=0 OR @hours>9999.99 OR @hours<>ROUND(@hours,2)
                OR @draft IS NULL OR @draft<=0 OR @draft>9999.99 OR @draft<>ROUND(@draft,2)
                OR @weight<0 OR @weight>9999999999.99 OR @weight<>ROUND(@weight,2) OR @teu<0
                OR @priority IS NULL OR @priority NOT BETWEEN 1 AND 5 OR @danger IS NULL OR DATALENGTH(@requirement)>2000
                OR (JSON_VALUE(@payload,'$.cargoWeightT') IS NOT NULL AND @weight IS NULL)
                OR (JSON_VALUE(@payload,'$.cargoTeu') IS NOT NULL AND @teu IS NULL)
                OR (JSON_VALUE(@payload,'$.previousPortId') IS NOT NULL AND @previous IS NULL)
                OR (JSON_VALUE(@payload,'$.nextPortId') IS NOT NULL AND @next IS NULL)
                OR (JSON_VALUE(@payload,'$.plannedDepartureAt') IS NOT NULL AND @planned_departure IS NULL)
                THROW 52100,'Invalid registration values.',1;
            IF @hours*3600>DATEDIFF_BIG(SECOND,@arrival,@departure) THROW 52106,'Operation hours exceed requested window.',1;
            IF (@action='SUBMIT' OR @submit=1) AND SWITCHOFFSET(@arrival,'+00:00')<SYSUTCDATETIME()
                THROW 52106,'Requested arrival is in the past.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.ship s WITH(HOLDLOCK) JOIN dbo.dictionary_item d WITH(HOLDLOCK) ON d.dictionary_item_id=s.ship_type_item_id
                WHERE s.ship_id=@ship AND s.status_code='ACTIVE' AND d.dictionary_type='SHIP_TYPE' AND d.is_enabled=1)
                THROW 52107,'Ship or ship type is unavailable.',1;
            IF @draft>(SELECT max_draft_m FROM dbo.ship WHERE ship_id=@ship) THROW 52106,'Declared draft exceeds ship maximum.',1;
            IF (@previous IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.port WITH(HOLDLOCK) WHERE port_id=@previous AND status_code='ENABLED'))
                OR (@next IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.port WITH(HOLDLOCK) WHERE port_id=@next AND status_code='ENABLED'))
                THROW 52107,'Selected port is unavailable.',1;
            DECLARE @now datetime2(0)=SYSUTCDATETIME();
            SET @status=CASE WHEN @action='SUBMIT' OR @submit=1 THEN 'PENDING' ELSE 'DRAFT' END;
            IF @action='CREATE'
            BEGIN
                INSERT dbo.port_call(call_no,ship_id,previous_port_id,next_port_id,planned_arrival_at,planned_departure_at,cargo_teu,cargo_weight_t,call_status_code)
                VALUES('PC'+REPLACE(CONVERT(varchar(36),@request_key),'-',''),@ship,@previous,@next,
                    CONVERT(datetime2(0),SWITCHOFFSET(@planned_arrival,'+00:00')),CONVERT(datetime2(0),SWITCHOFFSET(@planned_departure,'+00:00')),@teu,@weight,'PLANNED');
                SET @call_id=SCOPE_IDENTITY();
                INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,
                    declared_draft_m,special_requirement,dangerous_goods_flag,priority_level,appointment_status_code,submitted_at)
                VALUES(@no,@call_id,CONVERT(datetime2(0),SWITCHOFFSET(@arrival,'+00:00')),CONVERT(datetime2(0),SWITCHOFFSET(@departure,'+00:00')),
                    @hours,@draft,NULLIF(LTRIM(RTRIM(@requirement)),N''),@danger,@priority,@status,CASE WHEN @status='PENDING' THEN @now END);
                SET @appointment_id=SCOPE_IDENTITY();
            END
            ELSE IF @action='EDIT'
            BEGIN
                UPDATE dbo.port_call SET ship_id=@ship,previous_port_id=@previous,next_port_id=@next,
                    planned_arrival_at=CONVERT(datetime2(0),SWITCHOFFSET(@planned_arrival,'+00:00')),
                    planned_departure_at=CONVERT(datetime2(0),SWITCHOFFSET(@planned_departure,'+00:00')),cargo_teu=@teu,cargo_weight_t=@weight WHERE port_call_id=@call_id;
                UPDATE dbo.arrival_appointment SET requested_arrival_at=CONVERT(datetime2(0),SWITCHOFFSET(@arrival,'+00:00')),
                    requested_departure_at=CONVERT(datetime2(0),SWITCHOFFSET(@departure,'+00:00')),estimated_operation_hours=@hours,declared_draft_m=@draft,
                    special_requirement=NULLIF(LTRIM(RTRIM(@requirement)),N''),dangerous_goods_flag=@danger,priority_level=@priority WHERE appointment_id=@appointment_id;
            END
            ELSE UPDATE dbo.arrival_appointment SET appointment_status_code='PENDING',submitted_at=@now WHERE appointment_id=@appointment_id;

            IF @action IN('CREATE','EDIT')
                INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
                VALUES('port_call',CONVERT(varchar(100),@call_id),CASE WHEN @action='CREATE' THEN 'INSERT' ELSE 'UPDATE' END,@old_call,
                    (SELECT * FROM dbo.port_call WHERE port_call_id=@call_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@operator_id);
            DECLARE @after nvarchar(max)=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            IF @action='CREATE' SET @after=JSON_MODIFY(@after,'$.request_hash',@hash);
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
                VALUES('arrival_appointment',CONVERT(varchar(100),@appointment_id),CASE WHEN @action='CREATE' THEN 'INSERT' ELSE 'UPDATE' END,@old_appointment,@after,@operator_id);
        END;
        SELECT @version=COALESCE(MAX(audit_id),0) FROM dbo.audit_log WHERE
            (table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@appointment_id)) OR (table_name='port_call' AND record_id=CONVERT(varchar(100),@call_id));
        COMMIT TRANSACTION;
        SELECT CONVERT(varchar(20),@appointment_id) AppointmentId,@status AppointmentStatusCode,CONVERT(varchar(20),@version) EditVersion,@replayed Replayed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
