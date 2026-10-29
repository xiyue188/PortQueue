using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Infrastructure.Database;

namespace PortQueue.Infrastructure;

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
