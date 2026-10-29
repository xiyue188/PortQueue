using Microsoft.Data.SqlClient;

namespace PortQueue.Infrastructure.Database;

/// <summary>
/// 为 Dapper 查询和事务提供可替换的 SQL Server 连接创建抽象。
/// </summary>
public interface ISqlConnectionFactory
{
    SqlConnection Create();
}
