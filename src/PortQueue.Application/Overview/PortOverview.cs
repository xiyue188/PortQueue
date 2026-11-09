namespace PortQueue.Application.Overview;

/// <summary>全港只读摘要；在场数按实际进出锚事件确定，不等同于有效队列长度。</summary>
public sealed record PortOverview(long PendingAppointments, long AnchorageVessels,
    long ConfirmedPlans, long TotalEvents, int EventsPage, int EventsPageSize,
    IReadOnlyList<RecentPortEvent> RecentEvents, long WaitingQueue, long AssignedQueue,
    long DraftPlans, long ExecutingPlans, long PortEntries, long Berthings, long Unberthings,
    IReadOnlyList<AppointmentStateCount> AppointmentStates);

/// <summary>全量预约的互斥状态分布，包含零值；不能由近期摘要推算。</summary>
public sealed record AppointmentStateCount(string Code, long Count);

/// <summary>真实事件时间线的单项；主键用字符串传输，避免浏览器整数精度丢失。</summary>
public sealed record RecentPortEvent(string EventId, string EventTypeCode,
    DateTimeOffset EventTime, string ShipName, string AppointmentId, string AppointmentNo);

/// <summary>读取与预约筛选无关的港口工作量，不执行业务写操作。</summary>
public interface IOverviewQueries
{
    Task<PortOverview> GetAsync(int eventsPage, CancellationToken cancellationToken);
}
