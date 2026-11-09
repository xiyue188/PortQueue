namespace PortQueue.Application.Overview;

/// <summary>三个已接入查询页共享翻页协议；每个字段仍由对应业务视图提供。</summary>
public sealed record BusinessRecord(string Id, string AppointmentId, string AppointmentNo, string ShipName, string Location,
    string Status, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int? Position, int? Version);
public sealed record RegistrationOption(string Id, string Name, string Detail, decimal? MaxDraftM);
public sealed record QueryPage<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);
public interface IBusinessQueries
{
    Task<QueryPage<BusinessRecord>> RecordsAsync(string kind, string keyword, int page, CancellationToken token);
    Task<QueryPage<RegistrationOption>> OptionsAsync(string kind, string keyword, int page, CancellationToken token);
}
