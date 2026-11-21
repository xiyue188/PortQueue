/* F2: only confirmed versions are published facts. No schema/data mutation. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() <> N'PortQueueMobileDev' AND DB_NAME() NOT LIKE N'PortQueueStage2Test[_]%'
 THROW 52500,'Plan history requires isolated mobile/test database.',1;
GO
CREATE OR ALTER PROCEDURE dbo.usp_mobile_plan_history
 @appointment_id bigint,@page int=1,@page_size int=20
AS
BEGIN
 SET NOCOUNT ON;
 IF @appointment_id<=0 OR @page<1 OR @page_size NOT BETWEEN 1 AND 100
  THROW 52504,'Invalid history query.',1;
 ;WITH published AS (
  SELECT r.revision_id,r.berth_plan_id,r.version_no,b.berth_name,r.planned_berthing_at,
   r.planned_unberthing_at,r.confirmed_at,r.revision_reason,
   LAG(r.version_no) OVER(PARTITION BY r.berth_plan_id ORDER BY r.confirmed_at,r.revision_id) PreviousVersionNo,
   LAG(b.berth_name) OVER(PARTITION BY r.berth_plan_id ORDER BY r.confirmed_at,r.revision_id) PreviousBerthName,
   LAG(r.planned_berthing_at) OVER(PARTITION BY r.berth_plan_id ORDER BY r.confirmed_at,r.revision_id) PreviousBerthingAt,
   LAG(r.planned_unberthing_at) OVER(PARTITION BY r.berth_plan_id ORDER BY r.confirmed_at,r.revision_id) PreviousUnberthingAt
  FROM dbo.berth_plan p JOIN dbo.berth_plan_revision r ON r.berth_plan_id=p.berth_plan_id
  JOIN dbo.berth b ON b.berth_id=r.berth_id
  WHERE p.appointment_id=@appointment_id AND r.confirmed_at IS NOT NULL AND r.confirmed_by IS NOT NULL
 ) SELECT * INTO #published FROM published;
 SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.arrival_appointment WHERE appointment_id=@appointment_id) THEN 1 ELSE 0 END AS bit) AppointmentExists,
  COUNT_BIG(*) TotalCount,TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') ServerTime FROM #published;
 SELECT CONVERT(varchar(20),revision_id) RevisionId,version_no VersionNo,berth_name BerthName,
  TODATETIMEOFFSET(planned_berthing_at,'+00:00') PlannedBerthingAt,
  TODATETIMEOFFSET(planned_unberthing_at,'+00:00') PlannedUnberthingAt,
  TODATETIMEOFFSET(confirmed_at,'+00:00') PublishedAt,revision_reason Reason,
  PreviousVersionNo,PreviousBerthName,TODATETIMEOFFSET(PreviousBerthingAt,'+00:00') PreviousBerthingAt,
  TODATETIMEOFFSET(PreviousUnberthingAt,'+00:00') PreviousUnberthingAt
 FROM #published ORDER BY confirmed_at DESC,revision_id DESC
 OFFSET (CONVERT(bigint,@page)-1)*@page_size ROWS FETCH NEXT @page_size ROWS ONLY;
END;
GO
GRANT EXECUTE ON dbo.usp_mobile_plan_history TO portqueue_app;
GO
