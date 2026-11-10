using Dapper;
using Microsoft.Data.SqlClient;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>迁移仅触及明确的合成设施；保护航次前后港与用户预约，并验证重跑无额外审计。</summary>
[Trait("Category", "Database")]
public sealed class YangshanReferenceTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Fact]
    public async Task HomePortMigrationPreservesVisitsAndIsRepeatable()
    {
        await using var db = new SqlConnection(fixture.ConnectionString);
        await db.OpenAsync();
        // 构造上轮旧主港状态：复用测试库设施，保留旧港作为真实外键目标。
        await db.ExecuteAsync("""
            UPDATE dbo.port SET port_code='CNTAO',port_name=N'青岛港' WHERE port_code='SH-YS';
            DECLARE @old bigint=(SELECT port_id FROM dbo.port WHERE port_code='CNTAO');
            UPDATE dbo.terminal SET port_id=@old,terminal_name=N'Container Terminal A' WHERE terminal_code='T-A';
            UPDATE dbo.anchorage SET port_id=@old,anchorage_name=N'Outer Anchorage A',longitude=120.32,latitude=36.08 WHERE anchorage_code='ANCH-OUTER-A';
            UPDATE dbo.anchorage SET anchorage_name=N'Inner Anchorage A' WHERE anchorage_code='ANCH-INNER-A';
            UPDATE dbo.port_call SET previous_port_id=@old WHERE port_call_id=(SELECT MIN(port_call_id) FROM dbo.port_call);
            """);
        var calls = await db.QuerySingleAsync<string>("SELECT (SELECT * FROM dbo.port_call ORDER BY port_call_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS Payload");
        var appointments = await db.QuerySingleAsync<string>("SELECT (SELECT * FROM dbo.arrival_appointment ORDER BY appointment_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS Payload");
        var audit = await db.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.audit_log");
        await fixture.ApplyScriptAsync("15_yangshan_reference.sql");
        Assert.Equal(calls, await db.QuerySingleAsync<string>("SELECT (SELECT * FROM dbo.port_call ORDER BY port_call_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS Payload"));
        Assert.Equal(appointments, await db.QuerySingleAsync<string>("SELECT (SELECT * FROM dbo.arrival_appointment ORDER BY appointment_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS Payload"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.terminal t JOIN dbo.port p ON p.port_id=t.port_id WHERE p.port_code='SH-YS' AND t.terminal_code='T-A'"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id WHERE p.port_code='SH-YS' AND a.anchorage_code='ANCH-OUTER-A' AND a.longitude IS NULL AND a.latitude IS NULL"));
        Assert.Equal(audit + 6, await db.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.audit_log"));
        await fixture.ApplyScriptAsync("15_yangshan_reference.sql");
        await fixture.ApplyScriptAsync("05_seed_reference.sql");
        Assert.Equal(audit + 6, await db.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.audit_log"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.terminal WHERE terminal_code='T-A'"));
    }
}
