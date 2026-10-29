namespace PortQueue.Infrastructure.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string PortQueue { get; init; } = string.Empty;
}
