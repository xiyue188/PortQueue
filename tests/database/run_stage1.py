"""阶段一 SQL Server 集成验收。仅依赖 Python 标准库和 sqlcmd。

创建随机命名的独立数据库，部署真实 SQL、执行断言，最后只删除本次创建的测试库。
不修改 PortQueue。双连接用例使用两个独立 sqlcmd 进程，禁止用单连接模拟并发。
"""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
SQLCMD = shutil.which('sqlcmd') or r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
RESULTS = []


def run(sql, db, *, timeout=45, quoted_identifier=True):
    """sqlcmd -b 将 SQL THROW 变成失败；UTF-8 输入，所有错误保留原文。"""
    with tempfile.TemporaryDirectory(prefix='portqueue-sql-') as directory:
        path = Path(directory) / 'input.sql'
        path.write_text(sql, encoding='utf-8-sig')
        result = subprocess.run([SQLCMD, '-S', ARGS.server, '-E', '-C', '-b', *(['-I'] if quoted_identifier else []), '-l', '8',
                                 '-t', str(timeout), '-d', db, '-f', '65001', '-i', str(path)],
                                capture_output=True, encoding='utf-8', errors='replace', timeout=timeout+15)
    if result.returncode:
        raise AssertionError(result.stdout + result.stderr)
    return result.stdout


def deploy(db, name):
    # 只替换仓库固定的数据库上下文；测试数据库名由程序生成，不接受任意标识。
    sql = (ROOT/'database'/name).read_text(encoding='utf-8-sig')
    if name == '00_create_database.sql':
        sql = sql.replace('[PortQueue]', f'[{db}]').replace("N'PortQueue'", f"N'{db}'")
    # 部署脚本自带所需 SET 选项，不能被测试连接的 -I 默认为其兜底。
    return run(sql.replace('USE [PortQueue]', f'USE [{db}]'), db, quoted_identifier=False)


def assert_sql(condition, message):
    return f"IF NOT ({condition}) THROW 53999, '{message}', 1;\n"


def case(name, body, setup='', error=None):
    """每用例独立事务；错误断言检查真实业务错误号和外层事务已清理。"""
    sql = 'SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;\n' + FIXTURE + setup
    if error is None:
        sql += body + '\nROLLBACK TRANSACTION;'
    else:
        sql += f"""
        DECLARE @caught int=0;
        BEGIN TRY
            {body}
        END TRY
        BEGIN CATCH
            SET @caught=ERROR_NUMBER();
        END CATCH;
        DECLARE @remaining int=@@TRANCOUNT;
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        IF @caught<>{error} THROW 53998, 'Expected error {error} was not returned.', 1;
        IF {error} BETWEEN 52000 AND 52999 AND @remaining<>0 THROW 53997, 'Procedure did not clean up its transaction.', 1;
        IF @@TRANCOUNT<>0 THROW 53997, 'Transaction leaked.', 1;
        """
    started=time.monotonic()
    run(sql, DB)
    run("IF EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no LIKE 'S1-AP-%') THROW 53996,'Test rows leaked.',1;", DB)
    RESULTS.append({'case':name,'status':'passed','seconds':round(time.monotonic()-started,2)})
    print('PASS', name, flush=True)


FIXTURE = """
-- 历史用例夹具：独立设施和 4 艘船，事务内创建，测试结束回滚。
DECLARE @u bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
DECLARE @company bigint=(SELECT company_id FROM dbo.shipping_company WHERE company_code='DEMO-SC');
DECLARE @port bigint=(SELECT port_id FROM dbo.port WHERE port_code='CNTAO');
DECLARE @type bigint=(SELECT dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type='SHIP_TYPE' AND item_code='CONTAINER');
INSERT dbo.terminal(port_id,terminal_code,terminal_name,status_code) VALUES(@port,'S1-T',N'Test terminal','AVAILABLE');
DECLARE @terminal bigint=SCOPE_IDENTITY();
INSERT dbo.berth(terminal_id,berth_code,berth_name,length_m,max_draft_m,max_dwt,status_code)
VALUES(@terminal,'S1-B',N'Test berth',350,15,100000,'AVAILABLE');
DECLARE @b bigint=SCOPE_IDENTITY();
INSERT dbo.berth_ship_type VALUES(@b,@type,'ENABLED');
INSERT dbo.anchorage(port_id,anchorage_code,anchorage_name,anchorage_type_code,capacity_vessel_count,status_code)
VALUES(@port,'S1-A',N'Test anchorage','OUTER',4,'AVAILABLE');
DECLARE @a bigint=SCOPE_IDENTITY(),@i int=1,@s bigint,@c bigint;
DECLARE @apps TABLE(n int PRIMARY KEY,id bigint);
WHILE @i<=4
BEGIN
    INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
    VALUES(CONCAT('S1SHIP',@i),CONCAT('99000000',@i),CONCAT(N'Test Ship ',@i),@type,180,25,10,30000,@company,'ACTIVE');
    SET @s=SCOPE_IDENTITY();
    INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
    VALUES(CONCAT('S1-CALL-',@i),@s,'2026-01-01T00:00:00','2026-01-02T00:00:00','PLANNED');
    SET @c=SCOPE_IDENTITY();
    INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,
        estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code)
    VALUES(CONCAT('S1-AP-',@i),@c,'2026-01-01T00:00:00','2026-01-02T00:00:00',3,9,@i,'PENDING');
    INSERT @apps VALUES(@i,SCOPE_IDENTITY());
    SET @i+=1;
END;
DECLARE @ap1 bigint=(SELECT id FROM @apps WHERE n=1),@ap2 bigint=(SELECT id FROM @apps WHERE n=2),
        @ap3 bigint=(SELECT id FROM @apps WHERE n=3),@ap4 bigint=(SELECT id FROM @apps WHERE n=4);
DECLARE @p bigint,@r bigint,@q bigint;
"""
APPROVE = """
EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED';
EXEC dbo.usp_review_appointment @ap2,@u,'APPROVED';
EXEC dbo.usp_review_appointment @ap3,@u,'APPROVED';
EXEC dbo.usp_review_appointment @ap4,@u,'APPROVED';
"""
PLAN = """
EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T05:00:00',N'Initial',@u;
SELECT @p=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap1;
SELECT @r=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND is_current=1;
"""
CONFIRM = """
EXEC dbo.usp_confirm_berth_plan @p,@r,@u;
-- 夹具恢复历史确认时间；生产过程仍严格验证确认时间不晚于实际靠泊。
UPDATE dbo.berth_plan_revision SET revised_at='2025-12-30',confirmed_at='2025-12-31' WHERE revision_id=@r;
"""
QUEUE = """
EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_ENTER','2026-01-01T00:00:00',@u;
EXEC dbo.usp_record_anchorage_event @ap2,@a,'ANCHORAGE_ENTER','2026-01-01T00:10:00',@u;
EXEC dbo.usp_record_anchorage_event @ap3,@a,'ANCHORAGE_ENTER','2026-01-01T00:20:00',@u;
SELECT @q=queue_entry_id FROM dbo.anchorage_queue_entry WHERE appointment_id=@ap1;
"""


def business_cases():
    case('review_success_audit', "EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED';" + assert_sql(
        "EXISTS(SELECT 1 FROM dbo.audit_log WHERE table_name='arrival_appointment' AND record_id=CONVERT(varchar(100),@ap1) AND JSON_VALUE(old_data_json,'$.appointment_status_code')='PENDING' AND JSON_VALUE(new_data_json,'$.appointment_status_code')='APPROVED')", 'Review snapshot missing'))
    case('null_decision', "EXEC dbo.usp_review_appointment @ap1,@u,NULL;",error=52001)
    case('rejection_without_reason', "EXEC dbo.usp_review_appointment @ap1,@u,'REJECTED',N' ';",error=52004)
    case('disabled_operator', "EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED';", "UPDATE dbo.user_account SET status_code='DISABLED' WHERE user_id=@u;", 52091)
    case('repeated_review', "EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED';", APPROVE,52003)
    case('priority_and_manual_queue', """
        IF (SELECT queue_position FROM dbo.anchorage_queue_entry WHERE appointment_id=@ap3)<>1 THROW 53999,'Priority not applied.',1;
        EXEC dbo.usp_adjust_queue @q,1,N'Priority inspection',@u;
        IF (SELECT COUNT(*) FROM dbo.queue_adjustment_record WHERE adjusted_by=@u)<>3 THROW 53999,'Affected rows not recorded.',1;
        IF (SELECT queue_position FROM dbo.anchorage_queue_entry WHERE queue_entry_id=@q)<>1 THROW 53999,'Move failed.',1;
        EXEC dbo.usp_adjust_queue @q,3,N'Restore order',@u;
        IF (SELECT COUNT(*) FROM dbo.queue_adjustment_record WHERE adjusted_by=@u)<>6 THROW 53999,'Reverse move not recorded.',1;
    """, APPROVE+QUEUE)
    case('queue_bounds', "EXEC dbo.usp_adjust_queue @q,4,N'Bad',@u;", APPROVE+QUEUE,52052)
    case('queue_empty_reason', "EXEC dbo.usp_adjust_queue @q,1,N' ',@u;", APPROVE+QUEUE,52050)
    case('queue_no_change', "EXEC dbo.usp_adjust_queue @q,3,N'Same',@u;", APPROVE+QUEUE,52053)
    case('leave_compacts_queue', """
        EXEC dbo.usp_record_anchorage_event @ap2,@a,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;
        IF (SELECT MAX(queue_position) FROM dbo.anchorage_queue_entry WHERE anchorage_id=@a)<>2 THROW 53999,'Queue gap.',1;
    """, APPROVE+QUEUE)
    case('wrong_anchorage', "DECLARE @wrong bigint=@a+100; EXEC dbo.usp_record_anchorage_event @ap1,@wrong,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;", APPROVE+QUEUE,52027)
    case('repeat_enter', "EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_ENTER','2026-01-01T01:00:00',@u;",APPROVE+QUEUE,52022)
    case('capacity_includes_cancelled', "EXEC dbo.usp_record_anchorage_event @ap4,@a,'ANCHORAGE_ENTER','2026-01-01T01:00:00',@u;", APPROVE+QUEUE+"UPDATE dbo.anchorage SET capacity_vessel_count=3 WHERE anchorage_id=@a; UPDATE dbo.anchorage_queue_entry SET queue_status_code='CANCELLED',queue_position=NULL WHERE queue_entry_id=@q;",52026)
    case('cancelled_can_leave',"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;", APPROVE+QUEUE+"UPDATE dbo.anchorage_queue_entry SET queue_status_code='CANCELLED',queue_position=NULL WHERE queue_entry_id=@q;")
    case('draft_current_versions', """
        EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T06:00:00',N'New draft',@u;
        IF (SELECT COUNT(*) FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND is_current=1)<>1 THROW 53999,'Current count.',1;
        IF (SELECT version_no FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND is_current=1)<>2 THROW 53999,'Draft not current.',1;
    """,APPROVE+PLAN)
    case('confirmed_candidate_preserves_current', "EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T06:00:00',N'Candidate',@u; IF NOT EXISTS(SELECT 1 FROM dbo.berth_plan_revision WHERE revision_id=@r AND is_current=1) THROW 53999,'Current changed early.',1;",APPROVE+PLAN+CONFIRM)
    case('duplicate_confirmation', "EXEC dbo.usp_confirm_berth_plan @p,@r,@u;",APPROVE+PLAN+CONFIRM,52016)
    case('overlapping_window', """
        EXEC dbo.usp_create_berth_plan_revision @ap2,@b,'2026-01-01T04:00:00','2026-01-01T06:00:00',N'Conflict',@u;
        SELECT @p=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap2;
        SELECT @r=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@p;
        EXEC dbo.usp_confirm_berth_plan @p,@r,@u;
    """,APPROVE+PLAN+CONFIRM,52015)
    case('adjacent_window', """
        EXEC dbo.usp_create_berth_plan_revision @ap2,@b,'2026-01-01T05:00:00','2026-01-01T07:00:00',N'Adjacent',@u;
        SELECT @p=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap2;
        SELECT @r=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@p;
        EXEC dbo.usp_confirm_berth_plan @p,@r,@u;
    """,APPROVE+PLAN+CONFIRM)
    case('port_before_leave',"EXEC dbo.usp_record_port_entry @ap1,'2026-01-01T02:00:00',@u;",APPROVE+QUEUE,52042)
    case('berth_without_port_entry',"EXEC dbo.usp_record_berth_event @ap1,'BERTHING','2026-01-01T03:00:00',@r,@u;",APPROVE+PLAN+CONFIRM,52035)
    case('future_event',"DECLARE @future datetime2(0)=DATEADD(day,1,SYSUTCDATETIME()); EXEC dbo.usp_record_port_entry @ap1,@future,@u;",APPROVE,52092)
    port_entry="EXEC dbo.usp_record_port_entry @ap1,'2026-01-01T02:00:00',@u;"
    case('confirmation_after_actual_berthing',"EXEC dbo.usp_record_berth_event @ap1,'BERTHING','2026-01-01T03:00:00',@r,@u;",APPROVE+PLAN+"EXEC dbo.usp_confirm_berth_plan @p,@r,@u;"+port_entry,52036)
    berthed=APPROVE+PLAN+CONFIRM+port_entry+"EXEC dbo.usp_record_berth_event @ap1,'BERTHING','2026-01-01T03:10:00',@r,@u;"
    case('executing_baseline_preserved', """
        EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T07:00:00',N'Extend work',@u;
        DECLARE @r2 bigint=(SELECT revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND version_no=2);
        EXEC dbo.usp_confirm_berth_plan @p,@r2,@u;
        IF (SELECT plan_status_code FROM dbo.berth_plan WHERE berth_plan_id=@p)<>'EXECUTING' THROW 53999,'Executing state reset.',1;
        EXEC dbo.usp_record_berth_event @ap1,'UNBERTHING','2026-01-01T05:10:00',@r,@u;
        IF EXISTS(SELECT 1 FROM dbo.v_port_event WHERE appointment_id=@ap1 AND executed_revision_id<>@r) THROW 53999,'Baseline changed.',1;
        IF (SELECT appointment_status_code FROM dbo.arrival_appointment WHERE appointment_id=@ap1)<>'COMPLETED' THROW 53999,'Completion failed.',1;
    """,berthed)
    case('executing_cannot_move_start',"EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T04:00:00','2026-01-01T07:00:00',N'Invalid',@u;",berthed,52064)
    case('audit_update_rejected',"UPDATE dbo.audit_log SET operation_ip='127.0.0.1';",APPROVE,51013)
    case('audit_delete_rejected',"DELETE dbo.audit_log;",APPROVE,51013)
    case('dictionary_code_immutable',"UPDATE dbo.dictionary_item SET item_code='OTHER' WHERE dictionary_item_id=@type;",error=51012)
    case('disabled_dictionary_existing_reference_allowed',"UPDATE dbo.ship SET ship_type_item_id=@type WHERE ship_id=(SELECT MAX(ship_id) FROM dbo.ship);", # unchanged references remain legal
         "UPDATE dbo.dictionary_item SET is_enabled=0 WHERE dictionary_item_id=@type;")
    case('disabled_dictionary_reenable_relation',"UPDATE dbo.berth_ship_type SET relation_status='ENABLED' WHERE berth_id=@b;",
         "UPDATE dbo.dictionary_item SET is_enabled=0 WHERE dictionary_item_id=@type; UPDATE dbo.berth_ship_type SET relation_status='DISABLED' WHERE berth_id=@b;",51002)
    case('app_role_can_read_and_execute',"EXECUTE AS USER='portqueue_probe'; SELECT TOP(1) * FROM dbo.v_port_event; EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED'; REVERT;")
    case('app_role_cannot_write',"EXECUTE AS USER='portqueue_probe'; UPDATE dbo.arrival_appointment SET appointment_status_code='APPROVED' WHERE appointment_id=@ap1; REVERT;",error=229)
    case('app_role_cannot_read_hash',"EXECUTE AS USER='portqueue_probe'; SELECT password_hash FROM dbo.user_account; REVERT;",error=230)
    case('app_role_cannot_insert_orphan',"EXECUTE AS USER='portqueue_probe'; INSERT dbo.port_event(event_type_code,event_time,event_source_code,recorded_by) VALUES('PORT_ENTER','2026-01-01','MANUAL',@u); REVERT;",error=229)
    case('app_role_cannot_forge_audit',"EXECUTE AS USER='portqueue_probe'; INSERT dbo.audit_log(table_name,record_id,operation_type,operator_id) VALUES('ship','1','UPDATE',@u); REVERT;",error=229)
    case('left_queue_cannot_move',"EXEC dbo.usp_adjust_queue @q,1,N'Invalid',@u;",APPROVE+QUEUE+"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;",52051)
    case('leave_time_reversal',"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_LEAVE','2025-12-31',@u;",APPROVE+QUEUE,52024)
    case('repeat_leave',"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;",APPROVE+QUEUE+"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_LEAVE','2026-01-01T01:00:00',@u;",52023)
    case('repeat_port_entry',port_entry,APPROVE+port_entry,52041)
    case('repeat_berthing',"EXEC dbo.usp_record_berth_event @ap1,'BERTHING','2026-01-01T03:30:00',@r,@u;",berthed,52032)
    case('wrong_unberthing_baseline',"DECLARE @wrong bigint=@r+99; EXEC dbo.usp_record_berth_event @ap1,'UNBERTHING','2026-01-01T06:00:00',@wrong,@u;",berthed,52034)
    case('unberthing_time_reversal',"EXEC dbo.usp_record_berth_event @ap1,'UNBERTHING','2026-01-01T03:00:00',@r,@u;",berthed,52033)
    case('terminal_plan_revision',"EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T07:00:00',N'Invalid',@u;",APPROVE+PLAN+"UPDATE dbo.berth_plan SET plan_status_code='CANCELLED' WHERE berth_plan_id=@p;",52063)
    case('bad_plan_time',"EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-02','2026-01-01',N'Bad time',@u;",APPROVE,52060)
    case('unapproved_plan',PLAN,error=52012)
    case('berth_dimension_mismatch',"EXEC dbo.usp_confirm_berth_plan @p,@r,@u;",APPROVE+PLAN+"UPDATE dbo.berth SET max_draft_m=8 WHERE berth_id=@b;",52013)
    case('audit_failure_rolls_back_business',"EXEC dbo.usp_review_appointment @ap1,@u,'APPROVED';", "EXEC(N'CREATE TRIGGER dbo.trg_test_audit_failure ON dbo.audit_log AFTER INSERT AS THROW 52888, ''Injected audit failure'', 1;');",52888)
    case('disabled_dictionary_rejects_new_reference',"INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,shipping_company_id,status_code) VALUES('S1NEW01','990000099',N'Invalid new ship',@type,100,20,5,@company,'ACTIVE');", "UPDATE dbo.dictionary_item SET is_enabled=0 WHERE dictionary_item_id=@type;",51001)
    case('event_category_type_rejected',"INSERT dbo.anchorage_event_link(event_id,queue_entry_id) VALUES(@bad_event,@q);",APPROVE+QUEUE+"INSERT dbo.port_event(event_type_code,event_time,event_source_code,recorded_by) VALUES('PORT_ENTER','2026-01-01','MANUAL',@u); DECLARE @bad_event bigint=SCOPE_IDENTITY();",51003)
    case('event_type_immutable',"UPDATE dbo.port_event SET event_type_code='PORT_ENTER' WHERE event_id=(SELECT MIN(event_id) FROM dbo.port_event);",APPROVE+QUEUE,51011)


def concurrent_pair(first, second):
    """第一连接取得事务锁后通知主线程，再启动第二连接；以等待时长证明真实重叠。"""
    with tempfile.TemporaryDirectory(prefix='portqueue-parallel-') as directory:
        path=Path(directory)/'first.sql'
        path.write_text('SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;\n'+first+
                        "\nWAITFOR DELAY '00:00:05'; COMMIT;",encoding='utf-8-sig')
        process=subprocess.Popen([SQLCMD,'-S',ARGS.server,'-E','-C','-b','-I','-t','30','-d',DB,'-f','65001','-i',str(path)],
                                 stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace')
        try:
            # 不用 stdout 作为屏障：sqlcmd 的输出可能缓冲到进程结束。直接确认引擎已授锁。
            deadline=time.monotonic()+12
            while True:
                held=run("SET NOCOUNT ON; SELECT 'LOCK_HELD' FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_status='GRANT' AND request_session_id<>@@SPID;", DB)
                if 'LOCK_HELD' in held: break
                if process.poll() is not None or time.monotonic()>deadline:
                    raise AssertionError('First connection failed to hold a lock: '+process.communicate(timeout=5)[0])
                time.sleep(0.1)
            started=time.monotonic()
            run(second,DB)
            elapsed=time.monotonic()-started
            if elapsed<1: raise AssertionError('Second connection did not overlap lock holding interval')
            output=process.communicate(timeout=20)[0]
            if process.returncode: raise AssertionError(output)
        finally:
            if process.poll() is None: process.kill()


def concurrency_cases():
    # 此夹具故意提交，用于两个连接共享；只存在于随机测试库，结束随测试库清理。
    run('SET XACT_ABORT ON; BEGIN TRANSACTION;'+FIXTURE+APPROVE+'COMMIT;',DB)
    common="""
    DECLARE @u bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher');
    DECLARE @a bigint=(SELECT anchorage_id FROM dbo.anchorage WHERE anchorage_code='S1-A');
    DECLARE @b bigint=(SELECT berth_id FROM dbo.berth WHERE berth_code='S1-B');
    DECLARE @ap1 bigint=(SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no='S1-AP-1');
    DECLARE @ap2 bigint=(SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no='S1-AP-2');
    DECLARE @p bigint,@r bigint;
    """
    concurrent_pair(common+"EXEC dbo.usp_record_anchorage_event @ap1,@a,'ANCHORAGE_ENTER','2026-01-01',@u;",
                    common+"EXEC dbo.usp_record_anchorage_event @ap2,@a,'ANCHORAGE_ENTER','2026-01-01',@u;")
    run(common+"IF (SELECT COUNT(*) FROM dbo.anchorage_queue_entry WHERE anchorage_id=@a)<>2 OR (SELECT MAX(queue_position) FROM dbo.anchorage_queue_entry WHERE anchorage_id=@a)<>2 THROW 53999,'Concurrent entry failed.',1;",DB)
    RESULTS.append({'case':'two_connections_enter_same_anchorage','status':'passed'})
    adjust="DECLARE @q bigint=(SELECT queue_entry_id FROM dbo.anchorage_queue_entry WHERE appointment_id=@ap1); EXEC dbo.usp_adjust_queue @q,1,N'Concurrent move',@u;"
    concurrent_pair(common+adjust,common+"BEGIN TRY "+adjust+" THROW 53999,'Expected repeated move failure.',1; END TRY BEGIN CATCH IF ERROR_NUMBER()<>52053 THROW; END CATCH;")
    run("IF (SELECT COUNT(*) FROM dbo.queue_adjustment_record)<>2 THROW 53999,'Repeated concurrent move left records.',1;",DB)
    RESULTS.append({'case':'two_connections_adjust_same_queue','status':'passed'})
    run(common+"EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T05:00:00',N'Plan 1',@u; EXEC dbo.usp_create_berth_plan_revision @ap2,@b,'2026-01-01T04:00:00','2026-01-01T06:00:00',N'Plan 2',@u;",DB)
    one="SELECT @p=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap1; SELECT @r=revision_id FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND is_current=1; EXEC dbo.usp_confirm_berth_plan @p,@r,@u;"
    two=one.replace('@ap1','@ap2')
    concurrent_pair(common+one,common+"BEGIN TRY "+two+" THROW 53999,'Expected conflict.',1; END TRY BEGIN CATCH IF ERROR_NUMBER()<>52015 THROW; END CATCH;")
    run(common+"IF (SELECT COUNT(*) FROM dbo.berth_plan WHERE plan_status_code='CONFIRMED')<>1 THROW 53999,'Concurrent conflict was not rejected.',1; IF NOT EXISTS(SELECT 1 FROM dbo.berth_plan_revision r JOIN dbo.berth_plan p ON p.berth_plan_id=r.berth_plan_id WHERE p.appointment_id=@ap2 AND p.plan_status_code='DRAFT' AND r.is_current=1 AND r.confirmed_at IS NULL) THROW 53999,'Failed confirmation changed original state.',1;",DB)
    RESULTS.append({'case':'two_connections_confirm_conflicting_windows','status':'passed'})
    create=common+"EXEC dbo.usp_create_berth_plan_revision @ap1,@b,'2026-01-01T03:00:00','2026-01-01T07:00:00',N'Candidate',@u;"
    concurrent_pair(create,create)
    run(common+"SELECT @p=berth_plan_id FROM dbo.berth_plan WHERE appointment_id=@ap1; IF (SELECT MAX(version_no) FROM dbo.berth_plan_revision WHERE berth_plan_id=@p)<>3 OR (SELECT COUNT(*) FROM dbo.berth_plan_revision WHERE berth_plan_id=@p AND is_current=1)<>1 THROW 53999,'Concurrent versions failed.',1;",DB)
    RESULTS.append({'case':'two_connections_create_plan_versions','status':'passed'})
    print('PASS 4 two-connection scenarios',flush=True)


def structural_and_demo_cases():
    for name in ['06_smoke_tests.sql','07_business_smoke_test.sql','08_constraint_smoke_tests.sql']:
        deploy(DB,name)
    RESULTS.append({'case':'existing_06_07_08_regression','status':'passed'})
    for name in ['02_views.sql','03_triggers.sql','04_procedures.sql','05_seed_reference.sql','09_permissions.sql']:
        deploy(DB,name)
    run("""
        IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>21 THROW 53999,'Table count.',1;
        IF (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped=0)<>3 THROW 53999,'View count.',1;
        IF (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped=0)<>7 THROW 53999,'Procedure count.',1;
        IF (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped=0)<>8 THROW 53999,'Trigger count.',1;
        IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)
            OR EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1) THROW 53999,'Untrusted constraint.',1;
        IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=1) THROW 53999,'Disabled trigger.',1;
    """,DB)
    RESULTS.append({'case':'clean_restore_repeat_objects_and_constraint_trust','status':'passed'})
    deploy(DB,'10_seed_demo.sql')
    count_sql="SET NOCOUNT ON; SELECT t.name,SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE t.is_ms_shipped=0 GROUP BY t.name ORDER BY t.name;"
    before=run(count_sql,DB)
    deploy(DB,'10_seed_demo.sql')
    if run(count_sql,DB)!=before: raise AssertionError('Repeated demo load changed row counts')
    run("""
        IF (SELECT COUNT(*) FROM dbo.arrival_appointment WHERE appointment_no LIKE 'PQ-DEMO-%')<>5 THROW 53999,'Demo appointments.',1;
        IF (SELECT COUNT(*) FROM dbo.port_event)<>10 THROW 53999,'Demo events.',1;
        IF (SELECT COUNT(*) FROM dbo.v_port_event)<>(SELECT COUNT(*) FROM dbo.port_event) THROW 53999,'Orphan event.',1;
        IF EXISTS(SELECT 1 FROM dbo.port_event e LEFT JOIN dbo.anchorage_event_link a ON a.event_id=e.event_id
          LEFT JOIN dbo.port_entry_event_link p ON p.event_id=e.event_id LEFT JOIN dbo.berth_event_link b ON b.event_id=e.event_id
          WHERE (CASE WHEN a.event_id IS NULL THEN 0 ELSE 1 END+CASE WHEN p.event_id IS NULL THEN 0 ELSE 1 END+CASE WHEN b.event_id IS NULL THEN 0 ELSE 1 END)<>1)
          THROW 53999,'Event classification count.',1;
        IF (SELECT AVG(CONVERT(decimal(18,2),DATEDIFF(second,entered_anchorage_at,left_anchorage_at)))/60 FROM dbo.v_anchorage_queue WHERE left_anchorage_at IS NOT NULL)<>90 THROW 53999,'Wait metric.',1;
        IF (SELECT AVG(CONVERT(decimal(18,4),CASE WHEN e.event_time<=r.planned_berthing_at THEN 1 ELSE 0 END))*100
            FROM dbo.v_port_event e JOIN dbo.berth_plan_revision r ON r.revision_id=e.executed_revision_id WHERE e.event_type_code='BERTHING')<>50 THROW 53999,'On time metric.',1;
        IF (SELECT SUM(DATEDIFF(second,e.event_time,d.event_time))/60 FROM dbo.v_port_event e JOIN dbo.v_port_event d ON d.appointment_id=e.appointment_id AND d.event_type_code='UNBERTHING' WHERE e.event_type_code='BERTHING')<>250 THROW 53999,'Berth minutes metric.',1;
    """,DB)
    deploy(DB,'11_statistics.sql')
    RESULTS.append({'case':'demo_repeat_and_metrics_90min_50percent_250min','status':'passed'})
    # 推进一个待审核演示预约后重跑，保证装载器不会把它改回 PENDING。
    run("DECLARE @u bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'); DECLARE @ap bigint=(SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no='PQ-DEMO-005'); EXEC dbo.usp_review_appointment @ap,@u,'APPROVED';",DB)
    advanced=run(count_sql,DB)
    deploy(DB,'10_seed_demo.sql')
    if run(count_sql,DB)!=advanced: raise AssertionError('Demo reload changed progressed row counts')
    run("IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no='PQ-DEMO-005' AND appointment_status_code='APPROVED') THROW 53999,'Demo state reset.',1;",DB)
    RESULTS.append({'case':'demo_preserves_progressed_business_state','status':'passed'})
    # 重跑种子不得重新启用资料；只在当前独立测试库中修改。
    run("UPDATE dbo.dictionary_item SET is_enabled=0 WHERE dictionary_type='SHIP_TYPE' AND item_code='TANKER';",DB)
    deploy(DB,'05_seed_reference.sql')
    run("IF EXISTS(SELECT 1 FROM dbo.dictionary_item WHERE dictionary_type='SHIP_TYPE' AND item_code='TANKER' AND is_enabled=1) THROW 53999,'Seed reenabled reference.',1;",DB)
    RESULTS.append({'case':'reference_seed_preserves_disabled_status','status':'passed'})


def main():
    global DB
    DB='PortQueueStage1Test_'+uuid.uuid4().hex[:12]
    assert re.fullmatch(r'PortQueueStage1Test_[0-9a-f]{12}',DB)
    print('ISOLATED DATABASE',DB,flush=True)
    run(f'CREATE DATABASE [{DB}];', 'master')
    try:
        deploy(DB,'00_create_database.sql')
        for name in ['01_schema.sql','02_views.sql','03_triggers.sql','04_procedures.sql','05_seed_reference.sql','09_permissions.sql']:
            deploy(DB,name)
            print('DEPLOY',name,flush=True)
        business_cases()
        concurrency_cases()
        # 并发夹具已提交，演示验证在另一个空库中执行，避免两组数据混算指标。
        concurrency_db=DB
        DB='PortQueueStage1Test_'+uuid.uuid4().hex[:12]
        run(f'CREATE DATABASE [{DB}];','master')
        try:
            for name in ['01_schema.sql','02_views.sql','03_triggers.sql','04_procedures.sql','05_seed_reference.sql','09_permissions.sql']:
                deploy(DB,name)
            structural_and_demo_cases()
        finally:
            run(f'ALTER DATABASE [{DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DB}];','master')
            DB=concurrency_db
        report={'database':DB,'server':ARGS.server,'results':RESULTS}
        (ROOT/'artifacts').mkdir(exist_ok=True)
        (ROOT/'artifacts/stage1-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print(f'PASS {len(RESULTS)} cases',flush=True)
    finally:
        if not ARGS.keep_database:
            # 仅删除由本次运行生成并创建的数据库；不接受用户传入要删除的库名。
            run(f'ALTER DATABASE [{DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DB}];','master')
            print('REMOVED',DB,flush=True)
        else:
            print('KEPT',DB,flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server',default=r'(localdb)\PortQueueDev')
    parser.add_argument('--keep-database',action='store_true',help='保留本次新建测试库，供人工复核')
    ARGS=parser.parse_args()
    main()
