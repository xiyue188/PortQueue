using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure;

/// <summary>
/// 基础设施层的依赖注入入口；业务服务不在这里编排。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPortQueueInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        return services;
    }
}
