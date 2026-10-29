using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PortQueue.Infrastructure.Database;

public sealed class SqlConnectionFactory(IOptions<DatabaseOptions> options) : ISqlConnectionFactory
{
    // 连接字符串只从配置读取；业务 SQL 和事务边界由调用方明确控制。
    private readonly string _connectionString =
        string.IsNullOrWhiteSpace(options.Value.PortQueue)
            ? throw new InvalidOperationException("ConnectionStrings:PortQueue is required.")
            : options.Value.PortQueue;

    public SqlConnection Create() => new(_connectionString);
}
