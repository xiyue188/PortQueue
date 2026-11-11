using Microsoft.Data.SqlClient;
namespace PortQueue.Infrastructure.Mobile.Reports;
/// <summary>Commit boundary is injectable so tests can reproduce an acknowledgement lost after durable commit.</summary>
public interface IReportTransactionCommitter
{
    Task CommitAsync(SqlTransaction transaction, CancellationToken token);
}
public sealed class ReportTransactionCommitter : IReportTransactionCommitter
{
    public Task CommitAsync(SqlTransaction transaction, CancellationToken token) => transaction.CommitAsync(token);
}
