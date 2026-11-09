namespace PortQueue.Application.Appointments;

/// <summary>预约只读查询边界。写入业务仍由后续阶段的事务入口承担。</summary>
public interface IAppointmentQueries
{
    Task<AppointmentPage> GetPageAsync(AppointmentQuery query, CancellationToken cancellationToken);
    Task<AppointmentListItem?> GetByIdAsync(long appointmentId, CancellationToken cancellationToken);
}
