namespace PortQueue.Application.Appointments;

/// <summary>登记表单使用的启用资料候选，显示名称与实际引用标识分别传输。</summary>
public sealed record RegistrationOption(string Id, string Name, string Detail, decimal? MaxDraftM);
public sealed record QueryPage<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);
public interface IRegistrationOptions
{
    Task<QueryPage<RegistrationOption>> OptionsAsync(string kind, string keyword, int page, CancellationToken token);
}
