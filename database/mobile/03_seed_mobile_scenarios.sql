/* Synthetic M2 no-arrangement pair, same ship / distinct calls. Explicit opt-in only. */
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>N'PortQueueMobileDev' AND DB_NAME() NOT LIKE N'PortQueueStage2Test[_]%' THROW 52500,'Isolated mobile database required.',1;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource='mobile-scenarios',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 52509,'Scenario import busy.',1;
DECLARE @ship bigint=(SELECT MIN(ship_id) FROM dbo.ship WHERE status_code='ACTIVE'),@reviewer bigint=(SELECT MIN(user_id) FROM dbo.user_account WHERE role_code IN('ADMIN','REVIEWER') AND status_code='ENABLED');
IF @ship IS NULL OR @reviewer IS NULL THROW 52504,'Initialize core scenarios and demo accounts first.',1;
DECLARE @i int=1,@call bigint,@no varchar(40),@arrival datetime2(0)=DATEADD(day,1,CONVERT(date,SYSUTCDATETIME()));
WHILE @i<=2
BEGIN
 SET @no=CONCAT('M2-PHOTO-',@i);
 IF NOT EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_no=@no)
 BEGIN
  INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code) VALUES(CONCAT('M2-CALL-',@i),@ship,DATEADD(day,@i,@arrival),DATEADD(day,@i+1,@arrival),'PLANNED');
  SET @call=SCOPE_IDENTITY();
  INSERT dbo.arrival_appointment(appointment_no,port_call_id,requested_arrival_at,requested_departure_at,estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code,submitted_at,reviewed_by,reviewed_at,review_comment)
  VALUES(@no,@call,DATEADD(day,@i,@arrival),DATEADD(day,@i+1,@arrival),3,8,1,'APPROVED',SYSUTCDATETIME(),@reviewer,SYSUTCDATETIME(),N'移动照片场景初始化');
 END;
 SET @i+=1;
END;
COMMIT;
SELECT a.appointment_no,c.call_no,s.ship_name FROM dbo.arrival_appointment a JOIN dbo.port_call c ON c.port_call_id=a.port_call_id JOIN dbo.ship s ON s.ship_id=c.ship_id WHERE a.appointment_no IN('M2-PHOTO-1','M2-PHOTO-2');
