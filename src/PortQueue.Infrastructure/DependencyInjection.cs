using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortQueue.Infrastructure.Database;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Appointments;
using PortQueue.Application.Overview;
using PortQueue.Infrastructure.Overview;

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
        services.AddScoped<PortQueue.Application.Accounts.IAccountStore, PortQueue.Infrastructure.Accounts.SqlAccountStore>();
        services.AddScoped<IAppointmentQueries, SqlAppointmentQueries>();
        services.AddScoped<IAppointmentReviews, SqlAppointmentReviews>();
        services.AddScoped<IOverviewQueries, SqlOverviewQueries>();
        services.AddScoped<IAppointmentRegistration, SqlAppointmentRegistration>();
        services.AddScoped<IRegistrationOptions, SqlRegistrationOptions>();
        services.AddScoped<PortQueue.Application.Mobile.IMobileVisitQueries, PortQueue.Infrastructure.Mobile.SqlMobileVisitQueries>();
        services.AddSingleton<PortQueue.Infrastructure.Mobile.Reports.ReportPhotoStore>();
        services.AddSingleton<PortQueue.Infrastructure.Mobile.Reports.IReportTransactionCommitter,PortQueue.Infrastructure.Mobile.Reports.ReportTransactionCommitter>();
        services.AddScoped<PortQueue.Application.Mobile.Reports.IMobileReportService, PortQueue.Infrastructure.Mobile.Reports.SqlMobileReportService>();
        return services;
    }
}
