-- New showcase instance only. Add legal direct assignment and a second published plan.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() <> N'PortQueueMobileShowcase' THROW 52580,'Showcase database required.',1;
IF EXISTS(SELECT 1 FROM dbo.mobile_report) THROW 52581,'Never reinitialize an instance containing reports.',1;
BEGIN TRANSACTION;
DECLARE @dispatcher bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario.dispatcher'),
 @reviewer bigint=(SELECT user_id FROM dbo.user_account WHERE username='scenario.reviewer'),
 @direct bigint=(SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no='PQ202611-1002'),
 @main bigint=(SELECT appointment_id FROM dbo.arrival_appointment WHERE appointment_no='PQ202611-1003'),
 @berth bigint=(SELECT berth_id FROM dbo.berth WHERE berth_code='PQ-C01'),
 @plan bigint,@revision bigint,@start datetime2(0),@finish datetime2(0);
EXEC dbo.usp_review_appointment @direct,@reviewer,'APPROVED',N'船舶资料已齐备，按直接靠泊流程办理。';
DECLARE @created TABLE(berth_plan_id bigint,revision_id bigint,version_no int);
SELECT @start=DATEADD(hour,10,planned_berthing_at) FROM dbo.berth_plan_revision r JOIN dbo.berth_plan p ON p.berth_plan_id=r.berth_plan_id WHERE p.appointment_id=@main AND r.is_current=1;
SET @finish=DATEADD(hour,6,@start);
INSERT @created EXEC dbo.usp_create_berth_plan_revision @direct,@berth,@start,@finish,N'核对作业窗口后安排直接靠泊，无需进入候泊队列。',@dispatcher;
SELECT @plan=berth_plan_id,@revision=revision_id FROM @created;
EXEC dbo.usp_confirm_berth_plan @plan,@revision,@dispatcher;
DELETE FROM @created;
SELECT @start=DATEADD(minute,30,planned_berthing_at),@finish=DATEADD(minute,30,planned_unberthing_at) FROM dbo.berth_plan_revision r JOIN dbo.berth_plan p ON p.berth_plan_id=r.berth_plan_id WHERE p.appointment_id=@main AND r.is_current=1;
INSERT @created EXEC dbo.usp_create_berth_plan_revision @main,@berth,@start,@finish,N'前序作业交接延后半小时，靠离泊窗口同步顺延。',@dispatcher;
SELECT @plan=berth_plan_id,@revision=revision_id FROM @created;
EXEC dbo.usp_confirm_berth_plan @plan,@revision,@dispatcher;
COMMIT;
