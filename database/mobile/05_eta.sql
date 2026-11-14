/* M3 additive procedures/index. Run after M2; never changes formal schedules. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() <> N'PortQueueMobileDev' AND DB_NAME() NOT LIKE N'PortQueueStage2Test[_]%'
 THROW 52500, 'ETA migration requires isolated mobile/test database.', 1;
IF OBJECT_ID(N'dbo.mobile_report',N'U') IS NULL OR OBJECT_ID(N'dbo.usp_mobile_decide',N'P') IS NULL
 THROW 52500, 'Apply M2 before the ETA migration.', 1;
BEGIN TRANSACTION;
GO
CREATE OR ALTER PROCEDURE dbo.usp_mobile_submit_eta
 @user_id bigint,@request_id uniqueidentifier,@appointment_id bigint,@proposed_eta datetime2(7),
 @description nvarchar(max),@payload_hash binary(32)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 52501,'Transaction required.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code WHERE u.user_id=@user_id AND u.status_code='ENABLED' AND r.status_code='ENABLED' AND u.role_code IN('ADMIN','DISPATCHER','RECORDER')) THROW 52503,'Forbidden.',1;
 IF @request_id IS NULL OR @request_id='00000000-0000-0000-0000-000000000000' OR @payload_hash IS NULL OR @appointment_id IS NULL OR @appointment_id<=0 OR @description IS NULL OR LEN(LTRIM(RTRIM(@description))) NOT BETWEEN 1 AND 500
  THROW 52506,'Invalid ETA report.',1;
 DECLARE @resource nvarchar(255)=CONCAT('mobile-report:',@user_id,':',CONVERT(varchar(36),@request_id)),@lock int;
 EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
 IF @lock<0 THROW 52509,'Busy.',1;
 DECLARE @id bigint,@old_hash binary(32),@type varchar(10),@call bigint,@now datetime2(7);
 SELECT @id=report_id,@old_hash=payload_hash,@type=report_type FROM dbo.mobile_report WITH(UPDLOCK,HOLDLOCK) WHERE submitted_by=@user_id AND request_id=@request_id;
 IF @id IS NOT NULL
 BEGIN
  IF @old_hash<>@payload_hash OR @type<>'ETA' THROW 52509,'Request content conflict.',1;
  SELECT @id ReportId,CAST(1 AS bit) IsReplay; RETURN;
 END;
 SET @now=SYSUTCDATETIME();
 IF @proposed_eta IS NULL OR @proposed_eta<=@now OR @proposed_eta>DATEADD(day,30,@now) THROW 52506,'ETA must be in the next 30 days.',1;
 SELECT @call=pc.port_call_id FROM dbo.arrival_appointment a WITH(UPDLOCK,HOLDLOCK) JOIN dbo.port_call pc WITH(UPDLOCK,HOLDLOCK) ON pc.port_call_id=a.port_call_id WHERE a.appointment_id=@appointment_id AND a.appointment_status_code='APPROVED' AND pc.call_status_code IN('PLANNED','IN_PROGRESS');
 IF @call IS NULL THROW 52504,'Visit not eligible for submission.',1;
 INSERT dbo.mobile_report(request_id,port_call_id,report_type,description,proposed_eta,submitted_by,payload_hash)
 VALUES(@request_id,@call,'ETA',LTRIM(RTRIM(@description)),@proposed_eta,@user_id,@payload_hash);
 SET @id=SCOPE_IDENTITY();
 INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
 SELECT 'mobile_report',CONVERT(varchar(100),@id),'INSERT',(SELECT @id reportId,'ETA' reportType,'SUBMITTED' statusCode,CONVERT(varchar(33),@proposed_eta,126)+'Z' proposedEta FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@user_id;
 SELECT @id ReportId,CAST(0 AS bit) IsReplay;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_mobile_decide
 @user_id bigint,@report_id bigint,@decision varchar(12),@comment nvarchar(500),@expected_version binary(8)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 52501,'Transaction required.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code WHERE u.user_id=@user_id AND u.status_code='ENABLED' AND r.status_code='ENABLED' AND u.role_code IN('ADMIN','DISPATCHER')) THROW 52503,'Forbidden.',1;
 IF @decision IS NULL OR @decision NOT IN('ACCEPTED','REJECTED') OR @comment IS NULL OR LEN(LTRIM(RTRIM(@comment))) NOT BETWEEN 1 AND 500 THROW 52504,'Invalid decision.',1;
 DECLARE @type varchar(10),@eta datetime2(7),@status varchar(12),@version binary(8),@now datetime2(7);
 SELECT @type=report_type,@eta=proposed_eta,@status=status_code,@version=row_version FROM dbo.mobile_report WITH(UPDLOCK,HOLDLOCK) WHERE report_id=@report_id;
 IF @type IS NULL THROW 52505,'Not found.',1;
 IF @status<>'SUBMITTED' OR @expected_version IS NULL OR @version<>@expected_version THROW 52509,'Already processed or version conflict.',1;
 SET @now=SYSUTCDATETIME();
 IF @type='ETA' AND @decision='ACCEPTED' AND @eta<=@now THROW 52507,'ETA has expired; reject or request a new report.',1;
 UPDATE dbo.mobile_report SET status_code=@decision,process_comment=@comment,processed_by=@user_id,processed_at=@now WHERE report_id=@report_id AND status_code='SUBMITTED' AND row_version=@expected_version;
 IF @@ROWCOUNT<>1 THROW 52509,'Already processed or version conflict.',1;
 INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
 SELECT 'mobile_report',CONVERT(varchar(100),@report_id),'UPDATE',N'{"statusCode":"SUBMITTED"}',(SELECT @decision statusCode,@comment processComment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@user_id;
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.mobile_report') AND name='IX_mobile_report_accepted_eta')
 CREATE INDEX IX_mobile_report_accepted_eta ON dbo.mobile_report(port_call_id,processed_at DESC,report_id DESC) INCLUDE(proposed_eta) WHERE report_type='ETA' AND status_code='ACCEPTED';
GRANT EXECUTE ON dbo.usp_mobile_submit_eta TO portqueue_app;
COMMIT TRANSACTION;
GO
