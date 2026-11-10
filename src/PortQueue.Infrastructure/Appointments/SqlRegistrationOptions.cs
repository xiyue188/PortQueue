using System.Text.Json;
using Dapper;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure.Appointments;

/// <summary>参数化只读查询，以单SELECT返回总数及本页；数据库脚本为唯一SQL来源。</summary>
public sealed class SqlRegistrationOptions(ISqlConnectionFactory connections) : IRegistrationOptions
{
    private static readonly string Options = Load("RegistrationOptions");
    public Task<QueryPage<RegistrationOption>> OptionsAsync(string kind, string keyword, int page, CancellationToken token) => Read<RegistrationOption>(Options, kind, keyword, page, token);
    private async Task<QueryPage<T>> Read<T>(string sql, string kind, string keyword, int page, CancellationToken token)
    {
        await using var connection = connections.Create();
        var result = await connection.QuerySingleAsync<JsonPage>(new CommandDefinition(sql, new { Kind = kind, Keyword = keyword.Trim(), Offset = (page - 1) * 20 }, commandTimeout: 10, cancellationToken: token));
        return new(JsonSerializer.Deserialize<T[]>(result.ItemsJson) ?? [], result.TotalCount, page, 20);
    }
    private sealed record JsonPage(long TotalCount, string ItemsJson);
    private static string Load(string name)
    {
        using var stream = typeof(SqlRegistrationOptions).Assembly.GetManifestResourceStream($"PortQueue.Queries.{name}.sql") ?? throw new InvalidOperationException("Missing business SQL.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
