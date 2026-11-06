namespace PortQueue.Application.Appointments;

/// <summary>已由入口校验并归一化的预约筛选条件；不接收任意 SQL 排序字段。</summary>
public sealed record AppointmentQuery(int Page, int PageSize, string? Status, string? Keyword);
