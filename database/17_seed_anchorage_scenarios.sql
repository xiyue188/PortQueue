/*
   阶段4可选合成场景；已有库先16→04→09，14及SH-YS参考设施须就绪，最后按需执行本文件。
   不由启动流程自动装载。sqlcmd使用-b -f 65001，错误立即停止并保持中文编码。
   15次独立到访：外侧A入锚8艘、内侧A入锚4艘、已批准未入锚3艘。
   固定请求键40000000-0000-4000-8000-000000000001..015；过程生成PQ4/PC4编号。
   船名、尺寸与货物均为合成资料；IMO/MMSI为数字形式的虚构值，不声称号段保留或注册核验。
   整批事务及调度锁；重跑只补缺失预约，不重设已有状态、顺位、时间或审计。
   不回填历史：提交/审核由过程记录当前UTC，入锚在审核后登记，等待时长自然增长。
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

DECLARE @scenarios TABLE
(
    sequence_no int PRIMARY KEY, ship_name nvarchar(100), type_code varchar(30),
    length_m decimal(7,2), breadth_m decimal(7,2), max_draft_m decimal(6,2), deadweight_t decimal(12,2),
    cargo_teu int, cargo_weight_t decimal(12,2), draft_m decimal(6,2), operation_hours decimal(6,2),
    arrival_minutes int, window_hours int, priority_level tinyint, anchorage_code varchar(30),
    request_key uniqueidentifier, appointment_no varchar(40), call_no varchar(40), imo_no varchar(7), mmsi char(9)
);
INSERT @scenarios(sequence_no,ship_name,type_code,length_m,breadth_m,max_draft_m,deadweight_t,
    cargo_teu,cargo_weight_t,draft_m,operation_hours,arrival_minutes,window_hours,priority_level,anchorage_code)
VALUES
    (1, N'江澜远航','CONTAINER',228,32,12.2,52000,1240,17360,10.8,10,180,22,3,'ANCH-OUTER-A'),
    (2, N'东海晨光','BULK',210,32,12.8,58000,NULL,42600,11.6,16,240,28,2,'ANCH-OUTER-A'),
    (3, N'星洲明珠','CONTAINER',265,36,13.5,72000,1860,26040,11.9,14,300,26,4,'ANCH-OUTER-A'),
    (4, N'海棠长风','GENERAL',174,27,10.2,28000,NULL,18200,8.7,9,360,20,2,'ANCH-OUTER-A'),
    (5, N'远川海岳','BULK',225,33,13.0,64000,NULL,47100,11.7,18,420,30,3,'ANCH-OUTER-A'),
    (6, N'沧海云帆','CONTAINER',242,33,12.8,61000,1560,21840,11.2,12,480,24,2,'ANCH-OUTER-A'),
    (7, N'海岚青屿','CONTAINER',198,29,10.8,37000,880,11440,9.4,8,540,18,3,'ANCH-OUTER-A'),
    (8, N'蓝湾致远','GENERAL',182,28,10.6,31000,NULL,21300,9.1,11,600,23,1,'ANCH-OUTER-A'),
    (9, N'浦江清和','GENERAL',128,21,7.6,12500,NULL,7600,6.4,6,120,16,3,'ANCH-INNER-A'),
    (10,N'临港晓风','CONTAINER',155,24,9.1,21000,520,6760,7.8,7,210,18,2,'ANCH-INNER-A'),
    (11,N'东泽望潮','GENERAL',141,22,8.2,15800,NULL,9800,7.0,8,330,19,4,'ANCH-INNER-A'),
    (12,N'海宁嘉禾','CONTAINER',169,26,9.8,26500,720,9360,8.4,9,450,21,2,'ANCH-INNER-A'),
    (13,N'江澜启明','CONTAINER',221,31,11.8,47000,1180,15340,10.2,10,720,22,3,NULL),
    (14,N'东海瑞丰','BULK',204,31,12.0,52000,NULL,38400,10.7,15,900,28,2,NULL),
    (15,N'远川春晖','GENERAL',150,24,8.8,19000,NULL,12500,7.5,8,1080,20,2,NULL);
UPDATE @scenarios SET request_key=CONVERT(uniqueidentifier,
    CONCAT('40000000-0000-4000-8000-',RIGHT(CONCAT('000000000000',sequence_no),12)));
UPDATE @scenarios SET appointment_no='PQ'+REPLACE(CONVERT(varchar(36),request_key),'-',''),
    call_no='PC'+REPLACE(CONVERT(varchar(36),request_key),'-','');
-- 仅保持领域格式：IMO带校验位，MMSI九位。本库唯一不等于真实注册资料已核实。
UPDATE x SET imo_no=t.digits+CONVERT(varchar(1),
    (7*CONVERT(int,SUBSTRING(t.digits,1,1))+6*CONVERT(int,SUBSTRING(t.digits,2,1))
    +5*CONVERT(int,SUBSTRING(t.digits,3,1))+4*CONVERT(int,SUBSTRING(t.digits,4,1))
    +3*CONVERT(int,SUBSTRING(t.digits,5,1))+2*CONVERT(int,SUBSTRING(t.digits,6,1)))%10),
    mmsi=CONVERT(char(9),990040000+x.sequence_no)
FROM @scenarios x CROSS APPLY(VALUES(CONVERT(varchar(6),989400+x.sequence_no))) t(digits);

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock_result int;
    EXEC @lock_result=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',
        @LockOwner='Transaction',@LockTimeout=10000;
    IF @lock_result<0 THROW 53700,'Anchorage scenario import is busy.',1;

    -- 固定业务编号若已经属于其他资料，整批停止，不认领或覆盖既有记录。
    IF EXISTS(SELECT 1 FROM @scenarios x JOIN dbo.arrival_appointment a ON a.appointment_no=x.appointment_no
        JOIN dbo.port_call c ON c.port_call_id=a.port_call_id JOIN dbo.ship s ON s.ship_id=c.ship_id
        WHERE c.call_no<>x.call_no OR s.imo_no<>x.imo_no OR s.mmsi<>x.mmsi)
        THROW 53701,'Reserved stage 4 appointment number belongs to different source data.',1;

    IF EXISTS(SELECT 1 FROM @scenarios x WHERE NOT EXISTS(
        SELECT 1 FROM dbo.arrival_appointment a WHERE a.appointment_no=x.appointment_no))
    BEGIN
        IF COL_LENGTH('dbo.anchorage_queue_entry','manual_estimated_berthing_at') IS NULL
            OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.usp_record_anchorage_event') AND name='@home_port_code')
            THROW 53702,'Deploy stage 4 columns and procedures (16 then 04) before importing scenarios.',1;
        DECLARE @port_id bigint=(SELECT port_id FROM dbo.port WHERE port_code='SH-YS' AND status_code='ENABLED');
        IF @port_id IS NULL THROW 53703,'Enabled home port SH-YS is required.',1;
        IF EXISTS(SELECT 1 FROM @scenarios x WHERE NOT EXISTS(
            SELECT 1 FROM dbo.arrival_appointment a WHERE a.appointment_no=x.appointment_no)
            AND NOT EXISTS(SELECT 1 FROM dbo.dictionary_item d WHERE d.dictionary_type='SHIP_TYPE'
                AND d.item_code=x.type_code AND d.is_enabled=1))
            THROW 53704,'An enabled scenario ship type is missing.',1;

        -- 容量只按实际入锚且未离锚计算，撤销仍在场及非本批船舶也占用容量。
        IF EXISTS(SELECT 1 FROM @scenarios x LEFT JOIN dbo.anchorage a
            ON a.port_id=@port_id AND a.anchorage_code=x.anchorage_code
            WHERE x.anchorage_code IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM dbo.arrival_appointment ap WHERE ap.appointment_no=x.appointment_no)
            AND (a.anchorage_id IS NULL OR a.status_code<>'AVAILABLE'))
            THROW 53705,'A required home-port anchorage is unavailable.',1;
        IF EXISTS(SELECT 1 FROM dbo.anchorage a WHERE a.port_id=@port_id AND a.capacity_vessel_count IS NOT NULL
            AND EXISTS(SELECT 1 FROM @scenarios x WHERE x.anchorage_code=a.anchorage_code AND NOT EXISTS(
                SELECT 1 FROM dbo.arrival_appointment ap WHERE ap.appointment_no=x.appointment_no))
            AND a.capacity_vessel_count<
                (SELECT COUNT(*) FROM dbo.v_anchorage_queue q WHERE q.anchorage_id=a.anchorage_id
                    AND q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL)
                +(SELECT COUNT(*) FROM @scenarios x WHERE x.anchorage_code=a.anchorage_code AND NOT EXISTS(
                    SELECT 1 FROM dbo.arrival_appointment ap WHERE ap.appointment_no=x.appointment_no)))
            THROW 53706,'Existing occupancy leaves insufficient room; preserve existing records and free capacity through normal operations.',1;
        IF EXISTS(SELECT 1 FROM @scenarios x JOIN dbo.ship s ON s.imo_no=x.imo_no OR s.mmsi=x.mmsi
            WHERE NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment a WHERE a.appointment_no=x.appointment_no))
            THROW 53709,'A fictional scenario identifier already exists; do not reuse or overwrite another ship.',1;

        DECLARE @new_accounts TABLE(user_id bigint PRIMARY KEY);
        INSERT dbo.user_account(username,display_name,role_code,password_hash,status_code)
        OUTPUT inserted.user_id INTO @new_accounts
        SELECT x.username,x.display_name,x.role_code,CRYPT_GEN_RANDOM(32),'ENABLED'
        FROM (VALUES('scenario4.dispatcher',N'许澄','DISPATCHER'),('scenario4.reviewer',N'顾宁','REVIEWER'))
            x(username,display_name,role_code)
        WHERE NOT EXISTS(SELECT 1 FROM dbo.user_account u WHERE u.username=x.username);
        DECLARE @dispatcher_id bigint=(SELECT u.user_id FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
                WHERE u.username='scenario4.dispatcher' AND u.role_code='DISPATCHER' AND u.status_code='ENABLED' AND r.status_code='ENABLED'),
            @reviewer_id bigint=(SELECT u.user_id FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
                WHERE u.username='scenario4.reviewer' AND u.role_code='REVIEWER' AND u.status_code='ENABLED' AND r.status_code='ENABLED');
        IF @dispatcher_id IS NULL OR @reviewer_id IS NULL THROW 53707,'Scenario operator role or account is unavailable; existing accounts are not re-enabled.',1;
        -- 随机占位哈希不能登录；账号审计刻意不包含密码字段。
        INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
        SELECT 'user_account',CONVERT(varchar(100),u.user_id),'INSERT',
            (SELECT u.user_id,u.username,u.display_name,u.role_code,u.status_code,u.created_at
                FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@dispatcher_id
        FROM dbo.user_account u JOIN @new_accounts n ON n.user_id=u.user_id;

        IF NOT EXISTS(SELECT 1 FROM dbo.shipping_company WHERE company_code='PQ4-JH')
        BEGIN
            INSERT dbo.shipping_company(company_code,company_name,status_code) VALUES('PQ4-JH',N'江海联运有限公司','ACTIVE');
            DECLARE @new_company_id bigint=SCOPE_IDENTITY();
            INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
            VALUES('shipping_company',CONVERT(varchar(100),@new_company_id),'INSERT',
                (SELECT * FROM dbo.shipping_company WHERE company_id=@new_company_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@dispatcher_id);
        END;
        DECLARE @company_id bigint=(SELECT company_id FROM dbo.shipping_company WHERE company_code='PQ4-JH' AND status_code='ACTIVE');
        IF @company_id IS NULL THROW 53708,'Scenario shipping company is disabled.',1;

        DECLARE @n int=1,@request_key uniqueidentifier,@appointment_no varchar(40),@ship_id bigint,@appointment_id bigint,
            @anchorage_id bigint,@draft_version bigint,@payload nvarchar(max),@event_time datetime2(0),
            @base_time datetime2(0)=SYSUTCDATETIME(),@arrival datetime2(0),@departure datetime2(0);
        DECLARE @saved TABLE(AppointmentId varchar(20),AppointmentStatusCode varchar(30),EditVersion varchar(20),Replayed bit);
        DECLARE @reviewed TABLE(appointment_id bigint,appointment_status_code varchar(30));
        DECLARE @entered TABLE(event_id bigint,queue_entry_id bigint);
        WHILE @n<=15
        BEGIN
            SELECT @request_key=request_key,@appointment_no=appointment_no,
                @arrival=DATEADD(minute,arrival_minutes,@base_time),
                @departure=DATEADD(hour,window_hours,DATEADD(minute,arrival_minutes,@base_time))
            FROM @scenarios WHERE sequence_no=@n;
            IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no=@appointment_no)
            BEGIN
                -- 新批次应原子生成；候选键再次防止并发复用相同船舶标识。
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,
                    deadweight_t,shipping_company_id,status_code)
                SELECT x.imo_no,x.mmsi,
                    x.ship_name,d.dictionary_item_id,x.length_m,x.breadth_m,x.max_draft_m,x.deadweight_t,@company_id,'ACTIVE'
                FROM @scenarios x JOIN dbo.dictionary_item d ON d.dictionary_type='SHIP_TYPE' AND d.item_code=x.type_code AND d.is_enabled=1
                WHERE x.sequence_no=@n;
                SET @ship_id=SCOPE_IDENTITY();
                INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
                VALUES('ship',CONVERT(varchar(100),@ship_id),'INSERT',
                    (SELECT * FROM dbo.ship WHERE ship_id=@ship_id FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@dispatcher_id);
                SET @payload=(SELECT CONVERT(varchar(20),@ship_id) shipId,
                    TODATETIMEOFFSET(DATEADD(minute,-30,@arrival),'+00:00') plannedArrivalAt,
                    TODATETIMEOFFSET(DATEADD(hour,1,@departure),'+00:00') plannedDepartureAt,
                    TODATETIMEOFFSET(@arrival,'+00:00') requestedArrivalAt,TODATETIMEOFFSET(@departure,'+00:00') requestedDepartureAt,
                    x.cargo_teu cargoTeu,x.cargo_weight_t cargoWeightT,x.draft_m declaredDraftM,
                    x.operation_hours estimatedOperationHours,x.priority_level priorityLevel,CONVERT(bit,0) dangerousGoodsFlag
                    FROM @scenarios x WHERE x.sequence_no=@n FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
                INSERT @saved EXEC dbo.usp_save_appointment @action='CREATE',@operator_id=@dispatcher_id,
                    @payload=@payload,@request_key=@request_key,@submit=0;
                SELECT @appointment_id=appointment_id FROM dbo.arrival_appointment WHERE appointment_no=@appointment_no;
                SELECT @draft_version=CONVERT(bigint,EditVersion) FROM @saved WHERE AppointmentId=CONVERT(varchar(20),@appointment_id);
                INSERT @saved EXEC dbo.usp_save_appointment @action='SUBMIT',@operator_id=@dispatcher_id,
                    @appointment_id=@appointment_id,@expected_version=@draft_version;
                INSERT @reviewed EXEC dbo.usp_review_appointment @appointment_id=@appointment_id,@reviewer_id=@reviewer_id,
                    @decision='APPROVED',@review_comment=N'船舶资料、货物及申报吃水已核验。';
                SET @anchorage_id=NULL;
                SELECT @anchorage_id=a.anchorage_id FROM @scenarios x JOIN dbo.anchorage a
                    ON a.port_id=@port_id AND a.anchorage_code=x.anchorage_code WHERE x.sequence_no=@n;
                IF @anchorage_id IS NOT NULL
                BEGIN
                    SET @event_time=SYSUTCDATETIME();
                    INSERT @entered EXEC dbo.usp_record_anchorage_event @appointment_id=@appointment_id,@anchorage_id=@anchorage_id,
                        @event_type_code='ANCHORAGE_ENTER',@event_time=@event_time,@operator_id=@dispatcher_id,
                        @remark=N'抵达待泊水域，等待进港安排。',@home_port_code='SH-YS';
                END;
            END;
            SET @n+=1;
        END;
    END;
    COMMIT TRANSACTION;

    -- 显示当前事实而非写死初始数值；以后离锚、调序等操作应反映在结果中。
    SELECT a.anchorage_code,a.anchorage_name,a.capacity_vessel_count,
        COUNT(q.queue_entry_id) AS occupied_count,
        SUM(CASE WHEN q.queue_status_code IN('WAITING','ASSIGNED') THEN 1 ELSE 0 END) AS active_queue_count
    FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id
    LEFT JOIN dbo.v_anchorage_queue q ON q.anchorage_id=a.anchorage_id AND q.entered_anchorage_at IS NOT NULL AND q.left_anchorage_at IS NULL
    WHERE p.port_code='SH-YS' AND a.anchorage_code IN('ANCH-OUTER-A','ANCH-INNER-A','PQ-DEMO-A','PQ-OUTER')
    GROUP BY a.anchorage_code,a.anchorage_name,a.capacity_vessel_count ORDER BY a.anchorage_code;
    SELECT x.sequence_no,a.appointment_no,c.call_no,s.ship_name,a.appointment_status_code,a.reviewed_at,
        q.queue_entry_id,q.anchorage_name,q.queue_position,q.entered_anchorage_at,q.left_anchorage_at
    FROM @scenarios x JOIN dbo.arrival_appointment a ON a.appointment_no=x.appointment_no
    JOIN dbo.port_call c ON c.port_call_id=a.port_call_id JOIN dbo.ship s ON s.ship_id=c.ship_id
    LEFT JOIN dbo.v_anchorage_queue q ON q.appointment_id=a.appointment_id
    ORDER BY x.sequence_no,q.queue_entry_id;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
