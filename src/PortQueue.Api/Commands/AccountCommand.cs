using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using PortQueue.Api.Security;
using PortQueue.Application.Accounts;

namespace PortQueue.Api.Commands;

/// <summary>本机管理员维护入口。密码从隐藏键盘输入读取，测试可重定向 stdin；不写日志或文件。</summary>
public static partial class AccountCommand
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        if (args is ["seed-demo"]) return await DemoAccounts.SeedAsync(services);
        if (args.Length < 2 || args[0] is not ("create" or "reset-password" or "enable" or "disable")
            || !UsernamePattern().IsMatch(args[1]) || (args[0] == "create" && (args.Length != 4
                || !SessionSecurity.Roles.Contains(args[3]) || string.IsNullOrWhiteSpace(args[2]) || args[2].Length > 50))
            || (args[0] != "create" && args.Length != 2))
        {
            Console.Error.WriteLine("用法：account create <用户名> <姓名> <ADMIN|DISPATCHER|REVIEWER|RECORDER>；account reset-password|enable|disable <用户名>");
            return 2;
        }
        try
        {
            byte[]? hash = null;
            if (args[0] is "create" or "reset-password")
            {
                Console.Write("密码（6—128 字符，不回显）：");
                var password = ReadSecret();
                Console.Write("再次输入：");
                var confirmation = ReadSecret();
                if (password.Length is < 6 or > 128 || string.IsNullOrWhiteSpace(password) || password != confirmation)
                { Console.Error.WriteLine("密码长度不符合要求或两次输入不一致。"); return 2; }
                var hasher = services.GetRequiredService<IPasswordHasher<Account>>();
                hash = Encoding.UTF8.GetBytes(hasher.HashPassword(new Account(), password));
                if (hash.Length > 256) throw new InvalidOperationException("密码哈希超出存储边界。");
            }
            using var scope = services.CreateScope();
            var id = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ManageAsync(args[0], args[1],
                args[0] == "create" ? args[2] : null, args[0] == "create" ? args[3] : null, hash, CancellationToken.None);
            Console.WriteLine($"账号操作完成：{args[1]}（ID {id}）。");
            return 0;
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine(ex.Number switch { 53100 => "账号已存在，请使用 reset-password。", 53101 => "角色不存在或已停用。", 53102 => "账号不存在。", _ => "账号操作失败，请检查数据库连接与管理权限。" });
            return 1;
        }
    }
    private static string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace && value.Length > 0) value.Length--;
            else if (!char.IsControl(key.KeyChar) && value.Length < 129) value.Append(key.KeyChar);
        }
    }
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
