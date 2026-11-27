using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PortQueue.Application.Anchorages;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Anchorages;

public sealed class SqlAnchorageOperations(ISqlConnectionFactory connections, IConfiguration configuration) : IAnchorageOperations
{
    public Task<AnchorageEventResult> RecordAsync(long? appointmentId, long? anchorageId, long? queueEntryId,
        string eventType, DateTimeOffset eventTime, string? remark, long actor, CancellationToken cancellationToken) =>
        ExecuteAsync<AnchorageEventResult>("dbo.usp_record_anchorage_event", new
        {
            appointment_id = appointmentId, anchorage_id = anchorageId, queue_entry_id = queueEntryId,
            event_type_code = eventType, event_time = eventTime.UtcDateTime, operator_id = actor, remark,
            home_port_code = configuration["Port:Code"] ?? "SH-YS", return_details = true
        }, cancellationToken);

    public Task<AnchorageEstimateResult> SetEstimateAsync(long queueEntryId, DateTimeOffset? estimatedAt,
        long expectedVersion, string reason, long actor, CancellationToken cancellationToken) =>
        ExecuteAsync<AnchorageEstimateResult>("dbo.usp_set_anchorage_estimate", new
        {
            queue_entry_id = queueEntryId, estimated_at = estimatedAt?.UtcDateTime,
            expected_version = expectedVersion, reason, operator_id = actor,
            home_port_code = configuration["Port:Code"] ?? "SH-YS"
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(string procedure, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return await connection.QuerySingleAsync<T>(new CommandDefinition(procedure, parameters,
                commandType: CommandType.StoredProcedure, commandTimeout: 20, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is >= 52200 and <= 52213 or >= 52020 and <= 52028 or 52059 or 52090 or 52092)
        {
            var (code, message, status) = ex.Number switch
            {
                52201 => ("ANCHORAGE_FORBIDDEN", "当前账号没有此操作权限或已停用。", 403),
                52202 => ("SHIP_UNAVAILABLE", "船舶资料已停用，请先核对资料。", 409),
                52203 => ("ANCHORAGE_NOT_FOUND", "本港锚地或停留记录不存在。", 404),
                52204 => ("ESTIMATE_VERSION_CHANGED", "人工预计已被修改，请读取最新内容后再保存。", 409),
                52205 => ("STAY_NOT_ACTIVE", "该停留已离锚或已撤销，不能修改人工预计。", 409),
                52206 => ("SHIP_ALREADY_PRESENT", "同一船舶已有未结束的锚地或港内记录，请核对到访。", 409),
                52207 or 52023 => ("STAY_CHANGED", "所选停留已经结束或不存在，请读取最新记录；本次未登记离锚。", 409),
                52208 => ("ENTRY_BEFORE_APPROVAL", "实际入锚时间不能早于预约审核通过时间。", 400),
                52209 => ("ESTIMATE_REASON_REQUIRED", "请填写修改原因，最多500字。", 400),
                52210 => ("INVALID_ESTIMATE_TIME", "人工预计须为未来时间，并精确到秒。", 400),
                52211 or 52090 => ("ANCHORAGE_BUSY", "锚地正在办理其他操作，请稍后读取最新记录。", 409),
                52021 => ("APPOINTMENT_NOT_APPROVED", "预约尚未获批或到访已结束，不能登记入锚。", 409),
                52022 => ("STAY_ALREADY_OPEN", "该预约已有未结束停留，请先查看现有记录。", 409),
                52024 => ("ANCHORAGE_TIME_CONFLICT", "时间与既有停留重叠，或离锚时间早于入锚，请核对。", 400),
                52025 => ("ANCHORAGE_UNAVAILABLE", "锚地已受限、停用或所属港口已停用，不能登记新入锚。", 409),
                52026 => ("ANCHORAGE_CAPACITY_FULL", "锚地容量已满，请选择其他锚地或核对离锚记录。", 409),
                52027 => ("STAY_TARGET_MISMATCH", "停留与所选预约或锚地不匹配，请重新读取。", 409),
                52028 => ("VISIT_ALREADY_ENTERED_PORT", "本次到访已有实际进港记录，不能再次入锚。", 409),
                52092 => ("INVALID_EVENT_TIME", "实际时间不能为空或晚于当前时间。", 400),
                52059 or 52213 => ("ANCHORAGE_VALUE_EXHAUSTED", "当前记录无法继续调整，请联系管理员核对。", 409),
                _ => ("INVALID_ANCHORAGE_INPUT", "请检查时间精度、备注长度和登记内容。", 400)
            };
            throw new AnchorageOperationException(code, message, status);
        }
    }
}
