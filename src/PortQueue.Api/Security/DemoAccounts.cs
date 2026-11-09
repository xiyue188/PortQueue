using System.Text;
using Microsoft.AspNetCore.Identity;
using PortQueue.Application.Accounts;

namespace PortQueue.Api.Security;

/// <summary>用户明确授权的公开演示凭据；仅本地 Development 显式启用，仍经真实认证。</summary>
public static class DemoAccounts
{
    public const string DefaultPassword = "123456";
    public static readonly DemoAccount[] Presets = [
        new("pq.admin", "陈宁", "ADMIN"), new("pq.dispatcher", "周航", "DISPATCHER"),
        new("pq.reviewer", "林悦", "REVIEWER"), new("pq.recorder", "许舟", "RECORDER")];
    public static bool Enabled(IHostEnvironment environment, IConfiguration config) =>
        environment.IsDevelopment() && config.GetValue<bool>("DemoAccounts:Enabled");

    public static async Task<int> SeedAsync(IServiceProvider services)
    {
        if (!Enabled(services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IConfiguration>()))
        { Console.Error.WriteLine("仅允许在启用演示账号的 Development 环境初始化。"); return 2; }
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        var hasher = services.GetRequiredService<IPasswordHasher<Account>>();
        foreach (var preset in Presets)
        {
            // 已有账号绝不自动重置密码、角色或启停状态。
            if (await store.FindAsync(preset.Username, null, CancellationToken.None) is not null)
            { Console.WriteLine($"已保留：{preset.Username}"); continue; }
            var hash = Encoding.UTF8.GetBytes(hasher.HashPassword(new Account(), DefaultPassword));
            await store.ManageAsync("create", preset.Username, preset.DisplayName, preset.RoleCode, hash, CancellationToken.None);
            Console.WriteLine($"已创建：{preset.Username}（{SessionSecurity.RoleName(preset.RoleCode)}）");
        }
        Console.WriteLine("初始密码：123456。可在登录窗口选择账号。");
        return 0;
    }
}
public sealed record DemoAccount(string Username, string DisplayName, string RoleCode);
