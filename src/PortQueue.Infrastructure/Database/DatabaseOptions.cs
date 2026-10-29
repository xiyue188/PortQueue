namespace PortQueue.Infrastructure.Database;

/// <summary>
/// SQL Server 连接配置的强类型承载对象。
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string PortQueue { get; init; } = string.Empty;
}
