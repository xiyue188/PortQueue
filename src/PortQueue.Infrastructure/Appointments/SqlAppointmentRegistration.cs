using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Appointments;

/// <summary>所有变更通过登记过程，JSON按固定属性顺序序列化以稳定创建指纹。</summary>
public sealed class SqlAppointmentRegistration(ISqlConnectionFactory connections) : IAppointmentRegistration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<RegistrationResult> SaveAsync(string action, long actor, AppointmentRegistration? data, Guid? key,
        long? id, long? version, bool submit, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        try
        {
            return await connection.QuerySingleAsync<RegistrationResult>(new CommandDefinition("dbo.usp_save_appointment", new
            {
                action,
                operator_id = actor,
                payload = data is null ? null : JsonSerializer.Serialize(data, Json),
                request_key = key,
                appointment_id = id,
                expected_version = version,
                submit
            }, commandType: CommandType.StoredProcedure,
                commandTimeout: 20, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is >= 52100 and <= 52109)
        {
            var (code, message, status) = ex.Number switch
            {
                52101 => ("APPOINTMENT_NOT_FOUND", "预约不存在。", 404),
                52102 => ("DRAFT_STATE_CHANGED", "预约已提交或航次已开始，不能再修改草稿。", 409),
                52103 => ("REGISTER_FORBIDDEN", "当前账号没有登记权限或已停用。", 403),
                52104 => ("REQUEST_KEY_CONFLICT", "此登记请求已经使用，请先查看原预约。", 409),
                52105 => ("DRAFT_VERSION_CHANGED", "草稿已被其他窗口修改，请读取最新内容后再保存。", 409),
                52106 => ("INVALID_OPERATION_WINDOW", "请检查申请到港是否已过期、作业时长是否超过申请窗口，或申报吃水是否超过船舶上限。", 400),
                52107 => ("REFERENCE_UNAVAILABLE", "船舶、船型或港口已停用，请重新选择。", 400),
                52109 => ("REGISTRATION_BUSY", "预约正在处理中，请稍后重试。", 409),
                _ => ("INVALID_REGISTRATION", "登记内容无效，请检查时间、小数精度和字段范围。", 400)
            };
            throw new RegistrationException(code, message, status);
        }
    }
}
