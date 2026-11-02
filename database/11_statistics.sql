/*
   模块：统计口径验证查询（不新增视图或持久化指标）。
   UTC 半开统计区间 [from,to)，as_of 为等待/在泊实时观察时刻。
   平均等待：已离锚停留作为样本；在场等待另列，避免混合完整与未完成样本。
   靠泊偏差/准点率：以区间内靠泊事件为样本，沿 executed_revision_id 取固定基准。
   泊位占用率：实际在泊区间与统计区间相交秒数 / 泊位可用观测秒数。
   当前无维护区间历史，分母为当前未停用泊位的区间总秒数；明确是近似指标。
*/
USE [PortQueue];
GO
SET NOCOUNT ON;
DECLARE @from datetime2(0)='2026-11-01T00:00:00',@to datetime2(0)='2026-11-02T00:00:00',@as_of datetime2(0)=SYSUTCDATETIME();
IF @to<=@from THROW 53300,'Invalid report interval.',1;
SELECT COUNT(*) completed_stays,
    AVG(CONVERT(decimal(18,2),DATEDIFF_BIG(second,entered_anchorage_at,left_anchorage_at)))/60.0 avg_completed_wait_minutes
FROM dbo.v_anchorage_queue WHERE left_anchorage_at>=@from AND left_anchorage_at<@to;

SELECT queue_entry_id,appointment_no,queue_status_code,
    DATEDIFF_BIG(second,entered_anchorage_at,@as_of)/60.0 current_wait_minutes
FROM dbo.v_anchorage_queue WHERE entered_anchorage_at<=@as_of AND (left_anchorage_at IS NULL OR left_anchorage_at>@as_of);

SELECT e.appointment_id,e.executed_revision_id,
    DATEDIFF_BIG(second,r.planned_berthing_at,e.event_time)/60.0 arrival_deviation_minutes,
    DATEDIFF_BIG(second,e.event_time,d.event_time)/60.0 actual_berth_minutes
FROM dbo.v_port_event e JOIN dbo.berth_plan_revision r ON r.revision_id=e.executed_revision_id
LEFT JOIN dbo.v_port_event d ON d.appointment_id=e.appointment_id AND d.event_type_code='UNBERTHING'
WHERE e.event_type_code='BERTHING' AND e.event_time>=@from AND e.event_time<@to;

SELECT COUNT(*) berthed_calls,
    AVG(CONVERT(decimal(18,4),CASE WHEN e.event_time<=r.planned_berthing_at THEN 1 ELSE 0 END))*100 on_time_percent
FROM dbo.v_port_event e JOIN dbo.berth_plan_revision r ON r.revision_id=e.executed_revision_id
WHERE e.event_type_code='BERTHING' AND e.event_time>=@from AND e.event_time<@to;

;WITH stays AS (
    SELECT r.berth_id,e.event_time start_at,COALESCE(d.event_time,@as_of) end_at
    FROM dbo.v_port_event e JOIN dbo.berth_plan_revision r ON r.revision_id=e.executed_revision_id
    LEFT JOIN dbo.v_port_event d ON d.appointment_id=e.appointment_id AND d.event_type_code='UNBERTHING'
    WHERE e.event_type_code='BERTHING'
), clipped AS (
    SELECT berth_id,DATEDIFF_BIG(second,CASE WHEN start_at<@from THEN @from ELSE start_at END,
        CASE WHEN end_at>@to THEN @to ELSE end_at END) occupied_seconds
    FROM stays WHERE start_at<@to AND end_at>@from
)
SELECT b.berth_id,b.berth_name,COALESCE(SUM(c.occupied_seconds),0) occupied_seconds,
    100.0*COALESCE(SUM(c.occupied_seconds),0)/NULLIF(DATEDIFF_BIG(second,@from,@to),0) observed_occupancy_percent
FROM dbo.berth b LEFT JOIN clipped c ON c.berth_id=b.berth_id
WHERE b.status_code<>'DISABLED'
GROUP BY b.berth_id,b.berth_name;
GO
