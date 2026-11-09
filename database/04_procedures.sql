/*
   模块：七个事务业务入口。部署：03_triggers.sql 之后，可重复 CREATE OR ALTER。
   审计与业务同事务提交；失败回滚整个调用事务；返回值仅在调用成功后有效。
   并发：单港共用 PortQueue.Schedule 事务锁，API 不绕过这些入口直接写表。
*/
USE [PortQueue];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* usp_review_appointment: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_review_appointment
    @appointment_id bigint,
    @reviewer_id bigint,
    @decision varchar(20),
    @review_comment nvarchar(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.role r WITH(HOLDLOCK) ON r.role_code=u.role_code
            WHERE u.user_id=@reviewer_id AND u.status_code='ENABLED' AND r.status_code='ENABLED'
              AND u.role_code IN ('ADMIN','REVIEWER'))
            THROW 52092, 'An enabled reviewer or administrator is required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF @decision IS NULL OR @decision NOT IN ('APPROVED','REJECTED')
            THROW 52001, 'Decision must be APPROVED or REJECTED.', 1;
        SET @review_comment=NULLIF(LTRIM(RTRIM(@review_comment)),N'');
        IF @decision='REJECTED' AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@review_comment,CHAR(9),N''),CHAR(10),N''),CHAR(13),N''))),N'') IS NULL
            THROW 52004, 'Rejection requires a reason.', 1;
        DECLARE @status varchar(30);
        SELECT @status=appointment_status_code FROM dbo.arrival_appointment WITH(UPDLOCK,HOLDLOCK)
        WHERE appointment_id=@appointment_id;
        IF @status IS NULL THROW 52002, 'Appointment does not exist.', 1;
        IF @status<>'PENDING' THROW 52003, 'Only a pending appointment can be reviewed.', 1;
        SET @before=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.arrival_appointment SET appointment_status_code=@decision, reviewed_by=@reviewer_id,
            reviewed_at=@now, review_comment=@review_comment, submitted_at=COALESCE(submitted_at,@now)
        WHERE appointment_id=@appointment_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('arrival_appointment',CONVERT(varchar(100),@appointment_id),'UPDATE',@before,(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@reviewer_id);
        SELECT @appointment_id appointment_id, @decision appointment_status_code;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_adjust_queue: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_adjust_queue
    @queue_entry_id bigint,
    @new_position int,
    @adjustment_reason nvarchar(500),
    @operator_id bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF NULLIF(LTRIM(RTRIM(@adjustment_reason)),N'') IS NULL
            THROW 52050, 'Queue adjustment requires a reason.', 1;
        DECLARE @anchorage_id bigint,@old_position int,@count int;
        SELECT @anchorage_id=anchorage_id,@old_position=queue_position
        FROM dbo.anchorage_queue_entry WITH(UPDLOCK,HOLDLOCK)
        WHERE queue_entry_id=@queue_entry_id AND queue_status_code IN ('WAITING','ASSIGNED');
        IF @anchorage_id IS NULL THROW 52051, 'Queue entry is not active.', 1;
        SELECT @count=COUNT(*) FROM dbo.anchorage_queue_entry
        WHERE anchorage_id=@anchorage_id AND queue_status_code IN ('WAITING','ASSIGNED');
        IF @new_position IS NULL OR @new_position NOT BETWEEN 1 AND @count
            THROW 52052, 'Queue position is outside the active queue.', 1;
        IF @new_position=@old_position THROW 52053, 'Queue position has not changed.', 1;
        DECLARE @queue_before TABLE(queue_entry_id bigint PRIMARY KEY, data nvarchar(max));
        INSERT @queue_before SELECT q.queue_entry_id,
            (SELECT q.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER)
        FROM dbo.anchorage_queue_entry q WHERE q.anchorage_id=@anchorage_id;

        DECLARE @positions TABLE(id bigint PRIMARY KEY,old_position int,new_position int);
        INSERT @positions SELECT queue_entry_id,queue_position,
            CASE WHEN queue_entry_id=@queue_entry_id THEN @new_position
                 WHEN @new_position<@old_position AND queue_position BETWEEN @new_position AND @old_position-1 THEN queue_position+1
                 WHEN @new_position>@old_position AND queue_position BETWEEN @old_position+1 AND @new_position THEN queue_position-1
                 ELSE queue_position END
        FROM dbo.anchorage_queue_entry
        WHERE anchorage_id=@anchorage_id AND queue_status_code IN ('WAITING','ASSIGNED');
        -- 先移到不相交的正数区间，再写回目标序号；全程保留唯一索引。
        DECLARE @offset bigint=(SELECT COALESCE(MAX(COALESCE(queue_position,0)),0)+CONVERT(bigint,COUNT(*))+1
            FROM dbo.anchorage_queue_entry WHERE anchorage_id=@anchorage_id);
        IF @offset*2>2147483647 THROW 52059, 'Queue position range exhausted.', 1;
        UPDATE q SET queue_position=CONVERT(int,q.queue_position+@offset)
        FROM dbo.anchorage_queue_entry q JOIN @positions p ON p.id=q.queue_entry_id
        WHERE p.old_position<>p.new_position;
        UPDATE q SET queue_position=p.new_position, position_updated_at=@now
        FROM dbo.anchorage_queue_entry q JOIN @positions p ON p.id=q.queue_entry_id
        WHERE p.old_position<>p.new_position;

        DECLARE @adjustments TABLE(id bigint PRIMARY KEY);
        INSERT dbo.queue_adjustment_record(queue_entry_id,old_position,new_position,adjustment_reason,adjusted_by)
        OUTPUT inserted.adjustment_id INTO @adjustments
        SELECT id,old_position,new_position,@adjustment_reason,@operator_id
        FROM @positions WHERE old_position<>new_position;
        INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
        SELECT 'queue_adjustment_record',CONVERT(varchar(100),a.adjustment_id),'INSERT',
            (SELECT a.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id
        FROM dbo.queue_adjustment_record a JOIN @adjustments n ON n.id=a.adjustment_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        SELECT 'anchorage_queue_entry',CONVERT(varchar(100),q.queue_entry_id),
            CASE WHEN b.queue_entry_id IS NULL THEN 'INSERT' ELSE 'UPDATE' END,b.data,j.data,@operator_id
        FROM dbo.anchorage_queue_entry q
        LEFT JOIN @queue_before b ON b.queue_entry_id=q.queue_entry_id
        CROSS APPLY(SELECT (SELECT q.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER) data) j
        WHERE q.anchorage_id=@anchorage_id AND (b.data IS NULL OR b.data<>j.data);

        SELECT @queue_entry_id queue_entry_id,@new_position queue_position;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_create_berth_plan_revision: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_create_berth_plan_revision
    @appointment_id bigint,
    @berth_id bigint,
    @planned_berthing_at datetime2(0),
    @planned_unberthing_at datetime2(0),
    @revision_reason nvarchar(500),
    @operator_id bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF @planned_berthing_at IS NULL OR @planned_unberthing_at IS NULL
           OR @planned_unberthing_at<=@planned_berthing_at
            THROW 52060, 'Plan end must be after plan start.', 1;
        IF NULLIF(LTRIM(RTRIM(@revision_reason)),N'') IS NULL
            THROW 52061, 'A revision reason is required.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WITH(UPDLOCK,HOLDLOCK)
            WHERE appointment_id=@appointment_id AND appointment_status_code='APPROVED')
            THROW 52012, 'Only an approved appointment can have a plan.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.berth WHERE berth_id=@berth_id)
            THROW 52062, 'Berth does not exist.', 1;
        DECLARE @plan_id bigint,@status varchar(30),@version int,@revision_id bigint,@old_revision_id bigint;
        SELECT @plan_id=berth_plan_id,@status=plan_status_code FROM dbo.berth_plan WITH(UPDLOCK,HOLDLOCK)
        WHERE appointment_id=@appointment_id;
        IF @status IN ('COMPLETED','CANCELLED') THROW 52063, 'Terminal plan cannot be revised.', 1;
        IF @plan_id IS NULL
        BEGIN
            INSERT dbo.berth_plan(appointment_id,plan_status_code) VALUES(@appointment_id,'DRAFT');
            SET @plan_id=SCOPE_IDENTITY(); SET @status='DRAFT';
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan',CONVERT(varchar(100),@plan_id),'INSERT',NULL,(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@plan_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        END;
        SELECT @old_revision_id=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan_id AND is_current=1;
        IF @status='EXECUTING' AND EXISTS(SELECT 1 FROM dbo.berth_plan_revision
            WHERE revision_id=@old_revision_id AND (berth_id<>@berth_id OR planned_berthing_at<>@planned_berthing_at))
            THROW 52064, 'Executing plans may change only departure time.', 1;
        SELECT @version=COALESCE(MAX(version_no),0)+1 FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan_id;
        -- 草稿阶段的新版本直接成为当前；已确认/执行中候选在确认后切换。
        IF @status='DRAFT' AND @old_revision_id IS NOT NULL
        BEGIN
            SET @before=(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@old_revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.berth_plan_revision SET is_current=0 WHERE revision_id=@old_revision_id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan_revision',CONVERT(varchar(100),@old_revision_id),'UPDATE',@before,(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@old_revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        END;
        INSERT dbo.berth_plan_revision(berth_plan_id,version_no,berth_id,planned_berthing_at,
            planned_unberthing_at,revision_reason,revised_by,is_current)
        VALUES(@plan_id,@version,@berth_id,@planned_berthing_at,@planned_unberthing_at,
            @revision_reason,@operator_id,CASE WHEN @status='DRAFT' THEN 1 ELSE 0 END);
        SET @revision_id=SCOPE_IDENTITY();
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan_revision',CONVERT(varchar(100),@revision_id),'INSERT',NULL,(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        SELECT @plan_id berth_plan_id,@revision_id revision_id,@version version_no;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_confirm_berth_plan: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_confirm_berth_plan
    @berth_plan_id bigint,
    @revision_id bigint,
    @operator_id bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        DECLARE @appointment_id bigint,@status varchar(30),@berth_id bigint,@start datetime2(0),@end datetime2(0),
            @confirmed datetime2(0),@old_revision_id bigint,@version int,@old_version int;
        SELECT @appointment_id=p.appointment_id,@status=p.plan_status_code,@berth_id=r.berth_id,
            @start=r.planned_berthing_at,@end=r.planned_unberthing_at,@confirmed=r.confirmed_at,@version=r.version_no
        FROM dbo.berth_plan p WITH(UPDLOCK,HOLDLOCK) JOIN dbo.berth_plan_revision r ON r.berth_plan_id=p.berth_plan_id
        WHERE p.berth_plan_id=@berth_plan_id AND r.revision_id=@revision_id;
        IF @appointment_id IS NULL THROW 52010, 'Plan or matching revision does not exist.', 1;
        IF @status IN ('COMPLETED','CANCELLED') THROW 52011, 'Terminal plan cannot be confirmed.', 1;
        IF @confirmed IS NOT NULL THROW 52016, 'Revision has already been confirmed.', 1;
        SELECT @old_revision_id=revision_id,@old_version=version_no FROM dbo.berth_plan_revision
        WHERE berth_plan_id=@berth_plan_id AND is_current=1;
        IF @old_version>@version THROW 52017, 'Historical revisions cannot be restored.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id AND appointment_status_code='APPROVED')
            THROW 52012, 'Appointment must be approved.', 1;
        IF @status='EXECUTING' AND EXISTS(SELECT 1 FROM dbo.berth_plan_revision
            WHERE revision_id=@old_revision_id AND (berth_id<>@berth_id OR planned_berthing_at<>@start))
            THROW 52064, 'Executing plans may change only departure time.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment a JOIN dbo.port_call c ON c.port_call_id=a.port_call_id
            JOIN dbo.ship s ON s.ship_id=c.ship_id JOIN dbo.berth b ON b.berth_id=@berth_id
            JOIN dbo.terminal t ON t.terminal_id=b.terminal_id JOIN dbo.port p ON p.port_id=t.port_id
            WHERE a.appointment_id=@appointment_id AND s.status_code='ACTIVE' AND b.status_code='AVAILABLE'
            AND t.status_code='AVAILABLE' AND p.status_code='ENABLED' AND b.length_m>=s.length_m
            AND b.max_draft_m>=a.declared_draft_m AND s.max_draft_m>=a.declared_draft_m
            AND (b.max_dwt IS NULL OR (s.deadweight_t IS NOT NULL AND b.max_dwt>=s.deadweight_t)))
            THROW 52013, 'Resource availability or ship dimensions check failed.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment a JOIN dbo.port_call c ON c.port_call_id=a.port_call_id
            JOIN dbo.ship s ON s.ship_id=c.ship_id JOIN dbo.berth_ship_type b ON b.ship_type_item_id=s.ship_type_item_id
            JOIN dbo.dictionary_item d ON d.dictionary_item_id=b.ship_type_item_id
            WHERE a.appointment_id=@appointment_id AND b.berth_id=@berth_id AND b.relation_status='ENABLED'
              AND d.dictionary_type='SHIP_TYPE' AND d.is_enabled=1)
            THROW 52014, 'Berth ship type must be enabled.', 1;
        IF EXISTS(SELECT 1 FROM dbo.berth_plan_revision r JOIN dbo.berth_plan p ON p.berth_plan_id=r.berth_plan_id
            WHERE r.berth_id=@berth_id AND r.is_current=1 AND p.plan_status_code IN ('CONFIRMED','EXECUTING')
            AND p.berth_plan_id<>@berth_plan_id AND r.planned_berthing_at<@end AND r.planned_unberthing_at>@start)
            THROW 52015, 'Berth time window conflicts with another plan.', 1;
        IF @old_revision_id IS NOT NULL AND @old_revision_id<>@revision_id
        BEGIN
            SET @before=(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@old_revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.berth_plan_revision SET is_current=0 WHERE revision_id=@old_revision_id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan_revision',CONVERT(varchar(100),@old_revision_id),'UPDATE',@before,(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@old_revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        END;
        SET @before=(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.berth_plan_revision SET is_current=1,confirmed_by=@operator_id,confirmed_at=@now
        WHERE revision_id=@revision_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan_revision',CONVERT(varchar(100),@revision_id),'UPDATE',@before,(SELECT * FROM dbo.berth_plan_revision WHERE revision_id=@revision_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        SET @before=(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@berth_plan_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.berth_plan SET plan_status_code=CASE WHEN @status='EXECUTING' THEN 'EXECUTING' ELSE 'CONFIRMED' END,
            updated_at=@now WHERE berth_plan_id=@berth_plan_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan',CONVERT(varchar(100),@berth_plan_id),'UPDATE',@before,(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@berth_plan_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        DECLARE @queue_id bigint;
        SELECT @queue_id=queue_entry_id FROM dbo.anchorage_queue_entry
        WHERE appointment_id=@appointment_id AND queue_status_code IN ('WAITING','ASSIGNED');
        IF @queue_id IS NOT NULL
        BEGIN
            SET @before=(SELECT * FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@queue_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.anchorage_queue_entry SET queue_status_code='ASSIGNED',estimated_berthing_at=@start WHERE queue_entry_id=@queue_id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('anchorage_queue_entry',CONVERT(varchar(100),@queue_id),'UPDATE',@before,(SELECT * FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@queue_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        END;
        SELECT @berth_plan_id berth_plan_id,@revision_id revision_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_record_anchorage_event: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_record_anchorage_event
    @appointment_id bigint,
    @anchorage_id bigint,
    @event_type_code varchar(30),
    @event_time datetime2(0),
    @operator_id bigint,
    @remark nvarchar(500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF @event_type_code IS NULL OR @event_type_code NOT IN ('ANCHORAGE_ENTER','ANCHORAGE_LEAVE')
            THROW 52020, 'Anchorage operation must be enter or leave.', 1;
        IF @event_time IS NULL OR @event_time>@now THROW 52092, 'Event time cannot be null or in the future.', 1;
        DECLARE @queue_id bigint,@stay_anchorage bigint,@old_position int,@entered datetime2(0),@priority int;
        SELECT @queue_id=queue_entry_id,@stay_anchorage=anchorage_id,@entered=entered_anchorage_at
        FROM dbo.v_anchorage_queue WHERE appointment_id=@appointment_id AND left_anchorage_at IS NULL;
        DECLARE @queue_before TABLE(queue_entry_id bigint PRIMARY KEY, data nvarchar(max));
        INSERT @queue_before SELECT q.queue_entry_id,
            (SELECT q.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER)
        FROM dbo.anchorage_queue_entry q WHERE q.anchorage_id=@anchorage_id;

        DECLARE @positions TABLE(id bigint PRIMARY KEY,old_position int,new_position int);
        IF @event_type_code='ANCHORAGE_ENTER'
        BEGIN
            IF @queue_id IS NOT NULL THROW 52022, 'Appointment already has an unclosed stay.', 1;
            SELECT @priority=priority_level FROM dbo.arrival_appointment
            WHERE appointment_id=@appointment_id AND appointment_status_code='APPROVED';
            IF @priority IS NULL THROW 52021, 'Only an approved appointment can enter anchorage.', 1;
            IF EXISTS(SELECT 1 FROM dbo.port_entry_event_link WHERE appointment_id=@appointment_id)
                THROW 52028, 'A ship already in port cannot enter anchorage again.', 1;
            IF EXISTS(SELECT 1 FROM dbo.v_anchorage_queue WHERE appointment_id=@appointment_id AND left_anchorage_at>@event_time)
                THROW 52024, 'Anchorage stay times overlap.', 1;
            IF NOT EXISTS(SELECT 1 FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id
                WHERE a.anchorage_id=@anchorage_id AND a.status_code='AVAILABLE' AND p.status_code='ENABLED')
                THROW 52025, 'Anchorage is not available.', 1;
            IF EXISTS(SELECT 1 FROM dbo.anchorage a WHERE a.anchorage_id=@anchorage_id
                AND a.capacity_vessel_count IS NOT NULL AND a.capacity_vessel_count<=
                (SELECT COUNT(*) FROM dbo.v_anchorage_queue WHERE anchorage_id=@anchorage_id AND left_anchorage_at IS NULL))
                THROW 52026, 'Anchorage capacity is exhausted.', 1;
            DECLARE @tail int=(SELECT COALESCE(MAX(COALESCE(queue_position,0)),0)+1 FROM dbo.anchorage_queue_entry WHERE anchorage_id=@anchorage_id);
            -- 对新记录求插入点，保留已有人工调序的相对顺序。
            DECLARE @target int=(SELECT MIN(queue_position) FROM dbo.v_anchorage_queue
                WHERE anchorage_id=@anchorage_id AND queue_status_code IN ('WAITING','ASSIGNED')
                AND (priority_level<@priority OR (priority_level=@priority AND entered_anchorage_at>@event_time)));
            SET @target=COALESCE(@target,@tail);
            INSERT dbo.anchorage_queue_entry(appointment_id,anchorage_id,queue_position,priority_level,queue_status_code,position_updated_at)
            VALUES(@appointment_id,@anchorage_id,@tail,@priority,
                CASE WHEN EXISTS(SELECT 1 FROM dbo.berth_plan WHERE appointment_id=@appointment_id AND plan_status_code='CONFIRMED')
                     THEN 'ASSIGNED' ELSE 'WAITING' END,@now);
            SET @queue_id=SCOPE_IDENTITY();
            INSERT @positions SELECT queue_entry_id,queue_position,
                CASE WHEN queue_entry_id=@queue_id THEN @target WHEN queue_position>=@target THEN queue_position+1 ELSE queue_position END
            FROM dbo.anchorage_queue_entry WHERE anchorage_id=@anchorage_id AND queue_status_code IN ('WAITING','ASSIGNED');
        END
        ELSE
        BEGIN
            IF @queue_id IS NULL THROW 52023, 'Appointment has no unclosed anchorage stay.', 1;
            IF @stay_anchorage<>@anchorage_id OR @anchorage_id IS NULL THROW 52027, 'Leave anchorage does not match the stay.', 1;
            IF @entered IS NULL OR @event_time<@entered THROW 52024, 'Anchorage leave cannot precede enter.', 1;
            UPDATE dbo.anchorage_queue_entry SET queue_status_code='LEFT',queue_position=NULL,position_updated_at=@now WHERE queue_entry_id=@queue_id;
            INSERT @positions SELECT queue_entry_id,queue_position,ROW_NUMBER() OVER(ORDER BY queue_position)
            FROM dbo.anchorage_queue_entry WHERE anchorage_id=@anchorage_id AND queue_status_code IN ('WAITING','ASSIGNED');
        END;
        -- 先移到不相交的正数区间，再写回目标序号；全程保留唯一索引。
        DECLARE @offset bigint=(SELECT COALESCE(MAX(COALESCE(queue_position,0)),0)+CONVERT(bigint,COUNT(*))+1
            FROM dbo.anchorage_queue_entry WHERE anchorage_id=@anchorage_id);
        IF @offset*2>2147483647 THROW 52059, 'Queue position range exhausted.', 1;
        UPDATE q SET queue_position=CONVERT(int,q.queue_position+@offset)
        FROM dbo.anchorage_queue_entry q JOIN @positions p ON p.id=q.queue_entry_id
        WHERE p.old_position<>p.new_position;
        UPDATE q SET queue_position=p.new_position, position_updated_at=@now
        FROM dbo.anchorage_queue_entry q JOIN @positions p ON p.id=q.queue_entry_id
        WHERE p.old_position<>p.new_position;

        INSERT dbo.port_event(event_type_code,event_time,event_source_code,recorded_by,remark)
        VALUES(@event_type_code,@event_time,'MANUAL',@operator_id,@remark);
        DECLARE @event_id bigint=SCOPE_IDENTITY();
        INSERT dbo.anchorage_event_link(event_id,queue_entry_id) VALUES(@event_id,@queue_id);
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('port_event',CONVERT(varchar(100),@event_id),'INSERT',NULL,(SELECT e.*,l.queue_entry_id FROM dbo.port_event e JOIN dbo.anchorage_event_link l ON l.event_id=e.event_id WHERE e.event_id=@event_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        SELECT 'anchorage_queue_entry',CONVERT(varchar(100),q.queue_entry_id),
            CASE WHEN b.queue_entry_id IS NULL THEN 'INSERT' ELSE 'UPDATE' END,b.data,j.data,@operator_id
        FROM dbo.anchorage_queue_entry q
        LEFT JOIN @queue_before b ON b.queue_entry_id=q.queue_entry_id
        CROSS APPLY(SELECT (SELECT q.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER) data) j
        WHERE q.anchorage_id=@anchorage_id AND (b.data IS NULL OR b.data<>j.data);

        SELECT @event_id event_id,@queue_id queue_entry_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_record_port_entry: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_record_port_entry
    @appointment_id bigint,
    @event_time datetime2(0),
    @operator_id bigint,
    @remark nvarchar(500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF @event_time IS NULL OR @event_time>@now THROW 52092, 'Event time cannot be null or in the future.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id AND appointment_status_code='APPROVED')
            THROW 52040, 'Only an approved appointment can enter port.', 1;
        IF EXISTS(SELECT 1 FROM dbo.port_entry_event_link WHERE appointment_id=@appointment_id)
            THROW 52041, 'Appointment already has a port entry event.', 1;
        IF EXISTS(SELECT 1 FROM dbo.v_anchorage_queue WHERE appointment_id=@appointment_id
            AND (left_anchorage_at IS NULL OR left_anchorage_at>@event_time))
            THROW 52042, 'Port entry must follow all anchorage stays.', 1;
        INSERT dbo.port_event(event_type_code,event_time,event_source_code,recorded_by,remark)
        VALUES('PORT_ENTER',@event_time,'MANUAL',@operator_id,@remark);
        DECLARE @event_id bigint=SCOPE_IDENTITY();
        INSERT dbo.port_entry_event_link(event_id,appointment_id) VALUES(@event_id,@appointment_id);
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('port_event',CONVERT(varchar(100),@event_id),'INSERT',NULL,(SELECT e.*,l.appointment_id FROM dbo.port_event e JOIN dbo.port_entry_event_link l ON l.event_id=e.event_id WHERE e.event_id=@event_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        DECLARE @call_id bigint=(SELECT port_call_id FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id);
        SET @before=(SELECT * FROM dbo.port_call WHERE port_call_id=@call_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.port_call SET call_status_code='IN_PROGRESS' WHERE port_call_id=@call_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('port_call',CONVERT(varchar(100),@call_id),'UPDATE',@before,(SELECT * FROM dbo.port_call WHERE port_call_id=@call_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        SELECT @event_id event_id,@appointment_id appointment_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* usp_record_berth_event: 输入/前置条件见 docs/stages/stage-1-database.md；结果在提交前返回主键与状态。 */
CREATE OR ALTER PROCEDURE dbo.usp_record_berth_event
    @appointment_id bigint,
    @event_type_code varchar(30),
    @event_time datetime2(0),
    @executed_revision_id bigint,
    @operator_id bigint,
    @remark nvarchar(500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 单港课程基线：所有业务写入口共享事务锁；提交/回滚时自动释放。
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock @Resource=N'PortQueue.Schedule',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @lock_result < 0 THROW 52090, 'Schedule is busy. Retry the whole operation.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code
            WHERE u.user_id=@operator_id AND u.status_code='ENABLED' AND r.status_code='ENABLED')
            THROW 52091, 'An enabled operator and role are required.', 1;
        DECLARE @now datetime2(0)=SYSUTCDATETIME(), @before nvarchar(max);

        IF @event_type_code IS NULL OR @event_type_code NOT IN ('BERTHING','UNBERTHING')
            THROW 52030, 'Berth operation must be berthing or unberthing.', 1;
        IF @event_time IS NULL OR @event_time>@now THROW 52092, 'Event time cannot be null or in the future.', 1;
        DECLARE @plan_id bigint,@status varchar(30),@port_time datetime2(0),@berth_time datetime2(0),@baseline bigint;
        SELECT @plan_id=berth_plan_id,@status=plan_status_code FROM dbo.berth_plan WHERE appointment_id=@appointment_id;
        IF @plan_id IS NULL OR @status NOT IN ('CONFIRMED','EXECUTING')
            THROW 52031, 'Appointment has no executable confirmed plan.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id AND appointment_status_code='APPROVED')
            THROW 52031, 'Appointment is not approved.', 1;
        IF EXISTS(SELECT 1 FROM dbo.v_port_event WHERE appointment_id=@appointment_id AND event_type_code=@event_type_code)
            THROW 52032, 'Appointment already has this berth event.', 1;
        SELECT @port_time=event_time FROM dbo.v_port_event WHERE appointment_id=@appointment_id AND event_type_code='PORT_ENTER';
        IF @port_time IS NULL OR @event_time<@port_time THROW 52035, 'Berthing requires an earlier port entry.', 1;
        IF @event_type_code='BERTHING'
        BEGIN
            -- 按实际事件时间找最后确认版本，不能用查询时的当前版本重解释历史。
            SELECT TOP(1) @baseline=revision_id FROM dbo.berth_plan_revision
            WHERE berth_plan_id=@plan_id AND confirmed_at<=@event_time
            ORDER BY confirmed_at DESC,version_no DESC;
            IF @baseline IS NULL OR @executed_revision_id IS NULL OR @baseline<>@executed_revision_id
                THROW 52036, 'Execution baseline must be the last confirmed revision before berthing.', 1;
            -- 已有更晚的调整必须先补录事实再调整，避免把执行前候选误用于执行中。
            IF EXISTS(SELECT 1 FROM dbo.berth_plan_revision WHERE berth_plan_id=@plan_id AND confirmed_at>@event_time)
                THROW 52037, 'Record berthing before confirming later adjustments.', 1;
        END
        ELSE
        BEGIN
            SELECT @baseline=executed_revision_id,@berth_time=event_time FROM dbo.v_port_event
            WHERE appointment_id=@appointment_id AND event_type_code='BERTHING';
            IF @berth_time IS NULL OR @event_time<=@berth_time THROW 52033, 'Unberthing must be after berthing.', 1;
            IF @executed_revision_id IS NULL OR @baseline<>@executed_revision_id
                THROW 52034, 'Unberthing must use the original execution baseline.', 1;
        END;
        INSERT dbo.port_event(event_type_code,event_time,event_source_code,recorded_by,remark)
        VALUES(@event_type_code,@event_time,'MANUAL',@operator_id,@remark);
        DECLARE @event_id bigint=SCOPE_IDENTITY();
        INSERT dbo.berth_event_link(event_id,executed_revision_id) VALUES(@event_id,@executed_revision_id);
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('port_event',CONVERT(varchar(100),@event_id),'INSERT',NULL,(SELECT e.*,l.executed_revision_id FROM dbo.port_event e JOIN dbo.berth_event_link l ON l.event_id=e.event_id WHERE e.event_id=@event_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        SET @before=(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@plan_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.berth_plan SET plan_status_code=CASE WHEN @event_type_code='BERTHING' THEN 'EXECUTING' ELSE 'COMPLETED' END,
            updated_at=@now WHERE berth_plan_id=@plan_id;
        INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('berth_plan',CONVERT(varchar(100),@plan_id),'UPDATE',@before,(SELECT * FROM dbo.berth_plan WHERE berth_plan_id=@plan_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        IF @event_type_code='UNBERTHING'
        BEGIN
            SET @before=(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.arrival_appointment SET appointment_status_code='COMPLETED' WHERE appointment_id=@appointment_id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('arrival_appointment',CONVERT(varchar(100),@appointment_id),'UPDATE',@before,(SELECT * FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
            DECLARE @call_id bigint=(SELECT port_call_id FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id);
            SET @before=(SELECT * FROM dbo.port_call WHERE port_call_id=@call_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.port_call SET call_status_code='COMPLETED' WHERE port_call_id=@call_id;
            INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
        VALUES ('port_call',CONVERT(varchar(100),@call_id),'UPDATE',@before,(SELECT * FROM dbo.port_call WHERE port_call_id=@call_id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),@operator_id);
        END;
        SELECT @event_id event_id,@executed_revision_id executed_revision_id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- 不吞掉异常，也不让调用者继续提交半完成业务。外层事务一并回滚。
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
