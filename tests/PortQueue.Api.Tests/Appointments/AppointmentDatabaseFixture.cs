using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>在随机测试库部署真实脚本；只清理本次创建的库，绝不重置开发数据库。</summary>
public sealed class AppointmentDatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = "PortQueueStage2Test_" + Guid.NewGuid().ToString("N");
    private bool _created;
    public string ConnectionString { get; private set; } = string.Empty;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    private static string MasterConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("PORTQUEUE_TEST_SQL_CONNECTION")
                ?? @"Server=(localdb)\PortQueueDev;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5")
            {
                InitialCatalog = "master",
                Pooling = false
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ExecuteAsync(MasterConnectionString, $"CREATE DATABASE [{_databaseName}];");
            _created = true;
            ConnectionString = new SqlConnectionStringBuilder(MasterConnectionString)
                { InitialCatalog = _databaseName }.ConnectionString;
            var root = FindRepository();
            foreach (var file in new[] { "00_create_database.sql", "01_schema.sql", "02_views.sql",
                         "03_triggers.sql", "04_procedures.sql", "14_appointment_registration.sql", "05_seed_reference.sql",
                         "09_permissions.sql", "10_seed_demo.sql" })
            {
                var sql = (await File.ReadAllTextAsync(Path.Combine(root, "database", file)))
                    .Replace("[PortQueue]", $"[{_databaseName}]", StringComparison.Ordinal)
                    .Replace("N'PortQueue'", $"N'{_databaseName}'", StringComparison.Ordinal);
                // GO 是客户端批次标记；仅分割独占一行的 GO，使用同一连接保持 SET 选项。
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();
                foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase,
                             TimeSpan.FromSeconds(1)).Where(batch => !string.IsNullOrWhiteSpace(batch)))
                {
                    using var command = new SqlCommand(batch, connection) { CommandTimeout = 30 };
                    await command.ExecuteNonQueryAsync();
                }
            }

            await ExecuteAsync(ConnectionString, """
                SET QUOTED_IDENTIFIER ON;
                DECLARE @company bigint=(SELECT company_id FROM dbo.shipping_company WHERE company_code='DEMO-SC');
                DECLARE @type bigint=(SELECT dictionary_item_id FROM dbo.dictionary_item WHERE dictionary_type='SHIP_TYPE' AND item_code='CONTAINER');
                INSERT dbo.ship(imo_no,mmsi,ship_name,ship_type_item_id,length_m,breadth_m,max_draft_m,deadweight_t,shipping_company_id,status_code)
                VALUES('9999001','413999901',N'阶段二测试船',@type,180,25,10,30000,@company,'ACTIVE');
                DECLARE @ship bigint=SCOPE_IDENTITY(),@call bigint,@i int=1;
                SET IDENTITY_INSERT dbo.arrival_appointment ON;
                WHILE @i<=3
                BEGIN
                    INSERT dbo.port_call(call_no,ship_id,planned_arrival_at,planned_departure_at,call_status_code)
                    VALUES(CONCAT('S2-CALL-',@i),@ship,'2030-01-01','2030-01-02','PLANNED');
                    SET @call=SCOPE_IDENTITY();
                    INSERT dbo.arrival_appointment(appointment_id,appointment_no,port_call_id,requested_arrival_at,
                        requested_departure_at,estimated_operation_hours,declared_draft_m,priority_level,appointment_status_code)
                    VALUES(9007199254740992+CONVERT(bigint,@i),
                        CASE @i WHEN 1 THEN 'S2-TIE-A' WHEN 2 THEN 'S2-TIE-B' ELSE 'S2-%_[\-literal' END,
                        @call,'2030-01-01','2030-01-02',3,9,1,'DRAFT');
                    SET @i+=1;
                END;
                SET IDENTITY_INSERT dbo.arrival_appointment OFF;
                """);
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:PortQueue"] = ConnectionString })));
        }
        catch
        {
            await CleanupAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await CleanupAsync();
    }

    /// <summary>只向本夹具的随机独立库装载仓库脚本，供场景升级与幂等验收使用。</summary>
    public async Task ApplyScriptAsync(string filename)
    {
        var sql = (await File.ReadAllTextAsync(Path.Combine(FindRepository(), "database", filename)))
            .Replace("[PortQueue]", $"[{_databaseName}]", StringComparison.Ordinal);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase,
                     TimeSpan.FromSeconds(1)).Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task CleanupAsync()
    {
        if (!_created) return;
        if (!Regex.IsMatch(_databaseName, "^PortQueueStage2Test_[0-9a-f]{32}$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidOperationException("Unexpected test database name.");
        await ExecuteAsync(MasterConnectionString,
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];");
        _created = false;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PortQueue.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("PortQueue repository was not found.");
    }
}
