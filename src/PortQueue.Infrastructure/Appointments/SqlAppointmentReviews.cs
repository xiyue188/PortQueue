using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Appointments;

/// <summary>复用现有审核事务过程；数据库返回后才确认成功，不直接写表或自动重试。</summary>
public sealed class SqlAppointmentReviews(ISqlConnectionFactory connections) : IAppointmentReviews
{
    public async Task ReviewAsync(long appointmentId, long reviewerId, string decision, string? comment, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("dbo.usp_review_appointment", new
            { appointment_id = appointmentId, reviewer_id = reviewerId, decision, review_comment = comment },
                commandType: CommandType.StoredProcedure, commandTimeout: 20, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is 52001 or 52002 or 52003 or 52004 or 52090 or 52091 or 52092)
        {
            var (code, message) = ex.Number switch
            {
                52002 => ("APPOINTMENT_NOT_FOUND", "预约不存在。"),
                52003 => ("APPOINTMENT_STATE_CHANGED", "该预约已处理或已不处于待审核状态，请查看最新结果。"),
                52004 => ("REVIEW_REASON_REQUIRED", "驳回时请填写原因。"),
                52090 => ("REVIEW_BUSY", "预约正在处理中，请稍后查看最新状态。"),
                52091 or 52092 => ("REVIEW_FORBIDDEN", "当前账号没有审核权限或已停用。"),
                _ => ("INVALID_REVIEW", "请选择批准或驳回。")
            };
            throw new AppointmentReviewException(code, message);
        }
    }
}
