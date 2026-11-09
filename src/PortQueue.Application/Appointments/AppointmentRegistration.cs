namespace PortQueue.Application.Appointments;

/// <summary>登记的业务数据，不接受责任人、审核状态或审计时间。</summary>
public sealed record AppointmentRegistration(string ShipId, string? PreviousPortId, string? NextPortId,
    DateTimeOffset PlannedArrivalAt, DateTimeOffset? PlannedDepartureAt, int? CargoTeu, decimal? CargoWeightT,
    DateTimeOffset RequestedArrivalAt, DateTimeOffset RequestedDepartureAt, decimal EstimatedOperationHours,
    decimal DeclaredDraftM, byte PriorityLevel, bool DangerousGoodsFlag, string? SpecialRequirement);
public sealed record RegistrationResult(string AppointmentId, string AppointmentStatusCode, string EditVersion, bool Replayed);
public sealed class RegistrationException(string code, string message, int status) : Exception(message)
{ public string Code { get; } = code; public int Status { get; } = status; }
public interface IAppointmentRegistration
{
    Task<RegistrationResult> SaveAsync(string action, long actor, AppointmentRegistration? data, Guid? key,
        long? id, long? version, bool submit, CancellationToken cancellationToken);
}
