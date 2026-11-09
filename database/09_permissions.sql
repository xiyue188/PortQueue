/*
   模块：数据库最小权限。部署：04_procedures.sql 后，可重复执行。
   portqueue_probe 是 WITHOUT LOGIN 验证用户，不能用于网络登录。
   API 普通 Windows 身份由管理员另行 CREATE USER ... FOR LOGIN ... 并加入角色。
   LocalDB 所有者仍是管理员；本脚本不会假装限制 sysadmin 或存储任何密码。
*/
USE [PortQueue];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF DATABASE_PRINCIPAL_ID('portqueue_app') IS NULL CREATE ROLE portqueue_app AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID('portqueue_probe') IS NULL CREATE USER portqueue_probe WITHOUT LOGIN;
IF NOT EXISTS(SELECT 1 FROM sys.database_role_members WHERE role_principal_id=DATABASE_PRINCIPAL_ID('portqueue_app')
    AND member_principal_id=DATABASE_PRINCIPAL_ID('portqueue_probe'))
    ALTER ROLE portqueue_app ADD MEMBER portqueue_probe;

-- 禁止直接写表；同所有者的静态过程通过 ownership chaining 执行业务写入。
DENY INSERT,UPDATE,DELETE ON SCHEMA::dbo TO portqueue_app;
GRANT SELECT ON dbo.v_port_event TO portqueue_app;
GRANT SELECT ON dbo.v_anchorage_queue TO portqueue_app;
GRANT SELECT ON dbo.v_current_berth_plan TO portqueue_app;
GRANT EXECUTE ON dbo.usp_review_appointment TO portqueue_app;
GRANT EXECUTE ON dbo.usp_adjust_queue TO portqueue_app;
GRANT EXECUTE ON dbo.usp_create_berth_plan_revision TO portqueue_app;
GRANT EXECUTE ON dbo.usp_confirm_berth_plan TO portqueue_app;
GRANT EXECUTE ON dbo.usp_record_anchorage_event TO portqueue_app;
GRANT EXECUTE ON dbo.usp_record_port_entry TO portqueue_app;
GRANT EXECUTE ON dbo.usp_record_berth_event TO portqueue_app;

-- 阶段 2 所需的预约查询：明确列授权，不向应用开放明文密码或任意全库查询。
GRANT SELECT ON dbo.arrival_appointment(appointment_id,appointment_no,port_call_id,requested_arrival_at,
    requested_departure_at,appointment_status_code,reviewed_by,reviewed_at,review_comment) TO portqueue_app;
GRANT SELECT ON dbo.port_call(port_call_id,call_no,ship_id,call_status_code) TO portqueue_app;
GRANT SELECT ON dbo.ship(ship_id,ship_name,imo_no,mmsi) TO portqueue_app;
GRANT SELECT ON dbo.user_account(user_id,display_name) TO portqueue_app;
-- 阶段 3A：密码哈希仅由可信 API 验证，不进入 HTTP 响应；仍禁止直接写账号表。
GRANT SELECT ON dbo.user_account(username,role_code,password_hash,status_code) TO portqueue_app;
GRANT SELECT ON dbo.role(role_code,status_code) TO portqueue_app;
-- 阶段 3B 完整审核资料；基础资料停用不影响历史引用可读性。
GRANT SELECT ON dbo.arrival_appointment(estimated_operation_hours,declared_draft_m,priority_level,dangerous_goods_flag,special_requirement,submitted_at) TO portqueue_app;
GRANT SELECT ON dbo.port_call(planned_arrival_at,planned_departure_at,previous_port_id,next_port_id,cargo_teu,cargo_weight_t) TO portqueue_app;
GRANT SELECT ON dbo.ship(ship_type_item_id,shipping_company_id,length_m,breadth_m,max_draft_m,gross_tonnage,deadweight_t) TO portqueue_app;
GRANT SELECT ON dbo.dictionary_item(dictionary_item_id,item_name) TO portqueue_app;
GRANT SELECT ON dbo.shipping_company(company_id,company_name) TO portqueue_app;
GRANT SELECT ON dbo.port(port_id,port_name) TO portqueue_app;
-- 阶段3C：仍禁止直接写表，登记只授予过程；审计只暴露版本计算所需三列。
GRANT EXECUTE ON dbo.usp_save_appointment TO portqueue_app;
GRANT SELECT ON dbo.audit_log(audit_id,table_name,record_id) TO portqueue_app;
GRANT SELECT ON dbo.ship(status_code) TO portqueue_app;
GRANT SELECT ON dbo.dictionary_item(dictionary_type,is_enabled) TO portqueue_app;
GRANT SELECT ON dbo.port(port_code,country_code,timezone,status_code) TO portqueue_app;
GRANT SELECT ON dbo.terminal(terminal_id,port_id) TO portqueue_app;
GRANT SELECT ON dbo.anchorage(anchorage_id,port_id) TO portqueue_app;
GRANT SELECT ON dbo.berth(berth_id,terminal_id) TO portqueue_app;
COMMIT TRANSACTION;
GO
