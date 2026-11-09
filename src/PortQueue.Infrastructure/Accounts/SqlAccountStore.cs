using Dapper;
using PortQueue.Application.Accounts;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Accounts;

/// <summary>账号 SQL 唯一源在 database/queries；维护仅由本机管理命令调用，不暴露 HTTP 写入口。</summary>
public sealed class SqlAccountStore(ISqlConnectionFactory connections) : IAccountStore
{
    private static readonly string ReadSql = ReadResource("Account");
    private static readonly string ManageSql = ReadResource("ManageAccount");
    public async Task<Account?> FindAsync(string? username, long? userId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(ReadSql,
            new { Username = username, UserId = userId }, commandTimeout: 8, cancellationToken: cancellationToken));
    }
    public async Task<long> ManageAsync(string action, string username, string? displayName, string? roleCode,
        byte[]? passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleAsync<long>(new CommandDefinition(ManageSql,
            new { Action = action, Username = username, DisplayName = displayName, RoleCode = roleCode, PasswordHash = passwordHash },
            commandTimeout: 8, cancellationToken: cancellationToken));
    }
    private static string ReadResource(string name)
    {
        using var stream = typeof(SqlAccountStore).Assembly.GetManifestResourceStream($"PortQueue.Queries.{name}.sql")
            ?? throw new InvalidOperationException("Account SQL resource missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
