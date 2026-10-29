namespace PortQueue.Application.Abstractions;

public interface IDatabaseHealthService
{
    Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record DatabaseHealth(bool IsAvailable, string Provider, string? Detail);
