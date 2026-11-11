/* All writes through narrow ownership-chained procedures. Existing schema-wide write DENY stays. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() <> N'PortQueueMobileDev' AND DB_NAME() NOT LIKE N'PortQueueStage2Test[_]%'
 THROW 52500, 'Mobile permissions require isolated mobile/test database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.usp_mobile_submit_photo
 @user_id bigint,@request_id uniqueidentifier,@appointment_id bigint,@category varchar(10),@description nvarchar(500),
 @payload_hash binary(32),@storage_key varchar(40),@byte_size bigint,@captured_at datetime2(7),
 @latitude decimal(10,7)=NULL,@longitude decimal(10,7)=NULL,@accuracy_m float=NULL,@location_at datetime2(7)=NULL,@coordinate_system varchar(10)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 52501,'Transaction required.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.user_account u JOIN dbo.role r ON r.role_code=u.role_code WHERE u.user_id=@user_id AND u.status_code='ENABLED' AND r.status_code='ENABLED' AND u.role_code IN('ADMIN','DISPATCHER','RECORDER')) THROW 52503,'Forbidden.',1;
 DECLARE @resource nvarchar(255)=CONCAT('mobile-report:',@user_id,':',CONVERT(varchar(36),@request_id)),@lock int;
 EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
 IF @lock<0 THROW 52509,'Busy.',1;
 DECLARE @id bigint,@old_hash binary(32),@call bigint;
 SELECT @id=report_id,@old_hash=payload_hash FROM dbo.mobile_report WITH(UPDLOCK,HOLDLOCK) WHERE submitted_by=@user_id AND request_id=@request_id;
 IF @id IS NOT NULL
 BEGIN
  IF @old_hash<>@payload_hash THROW 52509,'Request content conflict.',1;
  SELECT @id ReportId,CAST(1 AS bit) IsReplay; RETURN;
 END;
 SELECT @call=pc.port_call_id FROM dbo.arrival_appointment a WITH(UPDLOCK,HOLDLOCK) JOIN dbo.port_call pc WITH(UPDLOCK,HOLDLOCK) ON pc.port_call_id=a.port_call_id WHERE a.appointment_id=@appointment_id AND a.appointment_status_code='APPROVED' AND pc.call_status_code IN('PLANNED','IN_PROGRESS');
 IF @call IS NULL THROW 52504,'Visit not eligible for submission.',1;
 INSERT dbo.mobile_report(request_id,port_call_id,report_type,category,description,submitted_by,payload_hash) VALUES(@request_id,@call,'PHOTO',@category,@description,@user_id,@payload_hash);
 SET @id=SCOPE_IDENTITY();
 INSERT dbo.report_photo(report_id,storage_key,mime_type,byte_size,captured_at,latitude,longitude,accuracy_m,location_at,coordinate_system)
 VALUES(@id,@storage_key,'image/jpeg',@byte_size,@captured_at,@latitude,@longitude,@accuracy_m,@location_at,@coordinate_system);
 INSERT dbo.audit_log(table_name,record_id,operation_type,new_data_json,operator_id)
 SELECT 'mobile_report',CONVERT(varchar(100),@id),'INSERT',(SELECT @id reportId,'SUBMITTED' statusCode FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@user_id;
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
 IF @decision NOT IN('ACCEPTED','REJECTED') OR @comment IS NULL OR LEN(LTRIM(RTRIM(@comment))) NOT BETWEEN 1 AND 500 THROW 52504,'Invalid decision.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.mobile_report WITH(UPDLOCK,HOLDLOCK) WHERE report_id=@report_id) THROW 52505,'Not found.',1;
 UPDATE dbo.mobile_report SET status_code=@decision,process_comment=@comment,processed_by=@user_id,processed_at=SYSUTCDATETIME() WHERE report_id=@report_id AND status_code='SUBMITTED' AND row_version=@expected_version;
 IF @@ROWCOUNT<>1 THROW 52509,'Already processed or version conflict.',1;
 INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
 SELECT 'mobile_report',CONVERT(varchar(100),@report_id),'UPDATE',N'{"statusCode":"SUBMITTED"}',(SELECT @decision statusCode,@comment processComment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@user_id;
END;
GO
GRANT SELECT ON dbo.mobile_report TO portqueue_app;
GRANT SELECT ON dbo.report_photo TO portqueue_app;
GRANT EXECUTE ON dbo.usp_mobile_submit_photo TO portqueue_app;
GRANT EXECUTE ON dbo.usp_mobile_decide TO portqueue_app;
GO
