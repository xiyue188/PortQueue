namespace PortQueue.Application.Appointments;

/// <summary>审核只通过事务入口，操作者由可信会话提供。成功结果不依赖随后详情读取。</summary>
public interface IAppointmentReviews
{
    Task ReviewAsync(long appointmentId, long reviewerId, string decision, string? comment, CancellationToken cancellationToken);
}
public sealed class AppointmentReviewException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
