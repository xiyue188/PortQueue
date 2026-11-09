namespace PortQueue.Application.Appointments;

/// <summary>审核所需完整申报；与列表协议分开，历史停用基础资料仍按原引用读取。</summary>
public sealed class AppointmentDetail
{
    public string AppointmentId { get; init; } = string.Empty;
    public string AppointmentNo { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string? ImoNo { get; init; }
    public string? Mmsi { get; init; }
    public DateTimeOffset RequestedArrivalAt { get; init; }
    public DateTimeOffset RequestedDepartureAt { get; init; }
    public string AppointmentStatusCode { get; init; } = string.Empty;
    public string? ReviewerName { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewComment { get; init; }
    public string ShipType { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public decimal LengthM { get; init; }
    public decimal BreadthM { get; init; }
    public decimal MaxDraftM { get; init; }
    public decimal? GrossTonnage { get; init; }
    public decimal? DeadweightT { get; init; }
    public string CallNo { get; init; } = string.Empty;
    public DateTimeOffset PlannedArrivalAt { get; init; }
    public DateTimeOffset? PlannedDepartureAt { get; init; }
    public string? PreviousPortName { get; init; }
    public string? NextPortName { get; init; }
    public int? CargoTeu { get; init; }
    public decimal? CargoWeightT { get; init; }
    public decimal EstimatedOperationHours { get; init; }
    public decimal DeclaredDraftM { get; init; }
    public byte PriorityLevel { get; init; }
    public bool DangerousGoodsFlag { get; init; }
    public string? SpecialRequirement { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
}
