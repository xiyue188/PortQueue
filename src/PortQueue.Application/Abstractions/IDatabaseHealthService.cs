namespace PortQueue.Application.Abstractions;

/// <summary>
/// 应用层数据库可用性检查契约；具体连接实现留在基础设施层。
/// </summary>
public interface IDatabaseHealthService
{
    Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record DatabaseHealth(bool IsAvailable, string Provider, string? Detail);
