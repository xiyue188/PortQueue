using Microsoft.Data.SqlClient;

namespace PortQueue.Infrastructure.Database;

public interface ISqlConnectionFactory
{
    SqlConnection Create();
}
