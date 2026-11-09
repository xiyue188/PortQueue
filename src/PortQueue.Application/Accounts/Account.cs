namespace PortQueue.Application.Accounts;

/// <summary>仅在可信后端使用的认证记录；不得作为 HTTP 响应返回。</summary>
public sealed class Account
{
    public long UserId { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string RoleCode { get; init; } = "";
    public string StatusCode { get; init; } = "";
    public string RoleStatus { get; init; } = "";
    public byte[] PasswordHash { get; init; } = [];
    public bool IsEnabled => StatusCode == "ENABLED" && RoleStatus == "ENABLED";
}

public interface IAccountStore
{
    Task<Account?> FindAsync(string? username, long? userId, CancellationToken cancellationToken);
    Task<long> ManageAsync(string action, string username, string? displayName, string? roleCode,
        byte[]? passwordHash, CancellationToken cancellationToken);
}
