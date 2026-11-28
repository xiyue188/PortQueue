/* 17号合成场景的配套整理（开发/验收库按17→18执行，不由应用启动自动执行）。
   简化已知设施名称；只修订原批量导入、尚未新增实际事件的12条合成停留。
   时间基于首次导入时刻确定，不随重跑漂移；记录时间created_at及原审计均保留。
   人工办理的时间、离锚/再次入锚、预计、调序和既有计划不重置。 */
USE [PortQueue];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int,@actor bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'),
        @dispatcher bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario4.dispatcher'),
        @reviewer bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario4.reviewer'),
        @batch varchar(40)='stage4-chronology-v1';
    EXEC @lock=sys.sp_getapplock @Resource=N'PortQueue.Schedule',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF @lock<0 OR @actor IS NULL THROW 53800,'Reference actor is missing or schedule is busy.',1;

    -- 只改明确的旧名称；保留主键、内部代码、容量、坐标和人工自定义名称。
    DECLARE @names TABLE(code varchar(30),old_name nvarchar(100),new_name nvarchar(100));
    INSERT @names VALUES('ANCH-INNER-A',N'内侧待泊锚地 A',N'内锚地'),
        ('ANCH-OUTER-A',N'外侧待泊锚地 A',N'外锚地1区'),
        ('PQ-DEMO-A',N'港外待泊锚地',N'外锚地2区'),('PQ-OUTER',N'外港待泊锚地',N'外锚地3区');
    DECLARE @renamed TABLE(id bigint,old_json nvarchar(max));
    INSERT @renamed SELECT a.anchorage_id,(SELECT a.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)
    FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id
    JOIN @names n ON n.code=a.anchorage_code AND n.old_name=a.anchorage_name WHERE p.port_code='SH-YS';
    UPDATE a SET anchorage_name=n.new_name FROM dbo.anchorage a JOIN @renamed r ON r.id=a.anchorage_id JOIN @names n ON n.code=a.anchorage_code;
    INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
    SELECT 'anchorage',CONVERT(varchar(100),r.id),'UPDATE',r.old_json,
        (SELECT a.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor
    FROM @renamed r JOIN dbo.anchorage a ON a.anchorage_id=r.id;

    DECLARE @n int=1,@appointment bigint,@queue bigint,@event bigint,@original datetime2(0),
        @entered datetime2(0),@reviewed datetime2(0),@submitted datetime2(0),@before nvarchar(max),
        @reason nvarchar(200)=N'修订17号合成样例批量导入造成的同刻时间链；保留原导入审计及记录时间。';
    WHILE @n<=12
    BEGIN
        SELECT @appointment=NULL,@queue=NULL,@event=NULL;
        SELECT @appointment=a.appointment_id,@queue=q.queue_entry_id,@event=e.event_id,@original=e.event_time
        FROM dbo.arrival_appointment a JOIN dbo.port_call c ON c.port_call_id=a.port_call_id
        JOIN dbo.ship s ON s.ship_id=c.ship_id JOIN dbo.shipping_company sc ON sc.company_id=s.shipping_company_id
        JOIN dbo.anchorage_queue_entry q ON q.appointment_id=a.appointment_id
        JOIN dbo.anchorage an ON an.anchorage_id=q.anchorage_id JOIN dbo.port p ON p.port_id=an.port_id
        JOIN dbo.anchorage_event_link l ON l.queue_entry_id=q.queue_entry_id JOIN dbo.port_event e ON e.event_id=l.event_id
        WHERE a.appointment_no=CONCAT('PQ40000000000040008000',RIGHT(CONCAT('000000000000',@n),12))
          AND c.call_no=CONCAT('PC40000000000040008000',RIGHT(CONCAT('000000000000',@n),12))
          AND sc.company_code='PQ4-JH' AND s.mmsi=CONVERT(char(9),990040000+@n)
          AND p.port_code='SH-YS' AND an.anchorage_code=CASE WHEN @n<=8 THEN 'ANCH-OUTER-A' ELSE 'ANCH-INNER-A' END
          AND a.appointment_status_code='APPROVED' AND a.reviewed_by=@reviewer
          AND q.queue_status_code IN('WAITING','ASSIGNED') AND e.event_type_code='ANCHORAGE_ENTER' AND e.recorded_by=@dispatcher
          AND DATEDIFF(second,a.submitted_at,e.event_time) BETWEEN 0 AND 5
          AND DATEDIFF(second,a.reviewed_at,e.event_time) BETWEEN 0 AND 5
          AND ABS(DATEDIFF(second,e.created_at,e.event_time))<=5
          AND ABS(DATEDIFF(second,q.created_at,e.event_time))<=5
          AND (SELECT COUNT(*) FROM dbo.v_port_event ev WHERE ev.appointment_id=a.appointment_id)=1
          AND (SELECT COUNT(*) FROM dbo.anchorage_queue_entry qe WHERE qe.appointment_id=a.appointment_id)=1
          AND NOT EXISTS(SELECT 1 FROM dbo.audit_log al WHERE al.table_name='arrival_appointment'
              AND al.record_id=CONVERT(varchar(100),a.appointment_id)
              AND (al.operator_id NOT IN(@dispatcher,@reviewer) OR JSON_VALUE(al.new_data_json,'$.seed_revision')=@batch));
        IF @appointment IS NOT NULL
        BEGIN
            -- 队列初始顺位与抵达先后一致，每船有独立的申请、审核、抵达间隔。
            SET @entered=DATEADD(minute,-(14-@n)*47,@original);
            SET @reviewed=DATEADD(minute,-(90+@n*13),@entered);
            SET @submitted=DATEADD(minute,-(240+@n*19),@reviewed);
            SET @before=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.arrival_appointment SET submitted_at=@submitted,reviewed_at=@reviewed WHERE appointment_id=@appointment;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
            VALUES('arrival_appointment',CONVERT(varchar(100),@appointment),'UPDATE',@before,
                (SELECT a.*,@batch seed_revision,@reason operation_reason FROM dbo.arrival_appointment a WHERE appointment_id=@appointment FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor);
            SET @before=(SELECT * FROM dbo.port_event WHERE event_id=@event FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.port_event SET event_time=@entered WHERE event_id=@event;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
            VALUES('port_event',CONVERT(varchar(100),@event),'UPDATE',@before,
                (SELECT e.*,@batch seed_revision,@reason operation_reason FROM dbo.port_event e WHERE event_id=@event FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor);
        END;
        SET @n+=1;
    END;

    -- 只补尚未办理的两类样例；通过原事务过程生成预计/计划/审计，不直接写状态。
    -- 仅对已通过来源核验的样例执行一次；v2允许期望窗口外的延后安排，保留已有v1审计。
    DECLARE @eligible TABLE(n int,appointment_id bigint,queue_id bigint);
    INSERT @eligible SELECT CONVERT(int,RIGHT(a.appointment_no,12)),a.appointment_id,q.queue_entry_id
    FROM dbo.arrival_appointment a JOIN dbo.v_anchorage_queue q ON q.appointment_id=a.appointment_id
    WHERE a.appointment_no IN('PQ40000000000040008000000000000001','PQ40000000000040008000000000000002',
        'PQ40000000000040008000000000000003','PQ40000000000040008000000000000006')
        AND q.left_anchorage_at IS NULL AND a.appointment_status_code='APPROVED'
        AND EXISTS(SELECT 1 FROM dbo.audit_log al WHERE al.table_name='arrival_appointment'
            AND al.record_id=CONVERT(varchar(100),a.appointment_id) AND JSON_VALUE(al.new_data_json,'$.seed_revision')=@batch)
        AND NOT EXISTS(SELECT 1 FROM dbo.audit_log al WHERE al.table_name='arrival_appointment'
            AND al.record_id=CONVERT(varchar(100),a.appointment_id) AND JSON_VALUE(al.new_data_json,'$.seed_revision')='stage4-variety-v2');
    DECLARE @at datetime2(0),@end datetime2(0),@berth bigint,@plan bigint,@revision bigint;
    WHILE EXISTS(SELECT 1 FROM @eligible)
    BEGIN
        SELECT TOP(1) @n=n,@appointment=appointment_id,@queue=queue_id FROM @eligible ORDER BY n;
        SET @at=DATEADD(hour,2+@n,CONVERT(datetime2(0),SYSUTCDATETIME()));
        IF @n IN(2,6) AND EXISTS(SELECT 1 FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@queue AND estimate_version=0 AND manual_estimated_berthing_at IS NULL)
            EXEC dbo.usp_set_anchorage_estimate @queue,@at,0,N'根据船方到达与待泊情况作初步估计。',@dispatcher;
        IF @n IN(1,3) AND NOT EXISTS(SELECT 1 FROM dbo.berth_plan WHERE appointment_id=@appointment)
        BEGIN
            SELECT @at=CASE WHEN requested_arrival_at>@at THEN requested_arrival_at ELSE @at END,
                @end=DATEADD(hour,estimated_operation_hours,CASE WHEN requested_arrival_at>@at THEN requested_arrival_at ELSE @at END)
            FROM dbo.arrival_appointment WHERE appointment_id=@appointment;
            SET @berth=NULL;
            SELECT TOP(1) @berth=b.berth_id FROM dbo.berth b JOIN dbo.terminal t ON t.terminal_id=b.terminal_id
            JOIN dbo.port p ON p.port_id=t.port_id JOIN dbo.berth_ship_type bt ON bt.berth_id=b.berth_id AND bt.relation_status='ENABLED'
            JOIN dbo.arrival_appointment a ON a.appointment_id=@appointment JOIN dbo.port_call c ON c.port_call_id=a.port_call_id
            JOIN dbo.ship s ON s.ship_id=c.ship_id AND s.ship_type_item_id=bt.ship_type_item_id
            WHERE p.port_code='SH-YS' AND b.status_code='AVAILABLE' AND t.status_code='AVAILABLE'
                AND b.length_m>=s.length_m AND b.max_draft_m>=a.declared_draft_m
                AND (b.max_dwt IS NULL OR b.max_dwt>=s.deadweight_t)
                AND NOT EXISTS(SELECT 1 FROM dbo.v_current_berth_plan bp WHERE bp.berth_id=b.berth_id
                    AND bp.plan_status_code IN('CONFIRMED','EXECUTING') AND bp.planned_berthing_at<@end AND bp.planned_unberthing_at>@at)
            ORDER BY b.berth_id;
            IF @berth IS NOT NULL
            BEGIN
                -- 申请窗口是船方期望；长期等待后安排可晚于期望，不能改申请掩盖偏差。
                EXEC dbo.usp_create_berth_plan_revision @appointment,@berth,@at,@end,N'合成场景：排队等待后的后续作业安排，原申请时间保留用于偏差核对。',@dispatcher;
                SELECT @plan=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@appointment;
                SELECT @revision=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan AND is_current=1;
                EXEC dbo.usp_confirm_berth_plan @plan,@revision,@dispatcher;
            END;
        END;
        -- 即使资源不足而跳过也留标记；不能在以后重跑时偷偷替用户安排。
        SET @before=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER);
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES('arrival_appointment',CONVERT(varchar(100),@appointment),'UPDATE',@before,
            (SELECT a.*,'stage4-variety-v2' seed_revision,N'合成场景可用性整理已检查，不修改预约字段。' operation_reason
                FROM dbo.arrival_appointment a WHERE appointment_id=@appointment FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),@actor);
        DELETE @eligible WHERE n=@n;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
