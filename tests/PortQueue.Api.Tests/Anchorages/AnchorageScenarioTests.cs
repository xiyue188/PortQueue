using Dapper;
using Microsoft.Data.SqlClient;
using PortQueue.Api.Tests.Appointments;

namespace PortQueue.Api.Tests.Anchorages;

/// <summary>样例整理也要遵守来源识别、审计和可重跑约定；只在随机测试库执行。</summary>
[Trait("Category", "Database")]
public sealed class AnchorageScenarioTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    [Fact]
    public async Task RefinesKnownImportWithoutResettingHumanChangesOrHistory()
    {
        await fixture.ApplyScriptAsync("15_yangshan_reference.sql");
        await fixture.ApplyScriptAsync("17_seed_anchorage_scenarios.sql");
        await using var db = new SqlConnection(fixture.ConnectionString);
        // 模拟样例上已有人工修订和预计：来源时间变化就跳过，既有预计不可覆盖。
        await db.ExecuteAsync("""
            UPDATE dbo.arrival_appointment SET submitted_at=DATEADD(hour,-1,submitted_at)
            WHERE appointment_no='PQ40000000000040008000000000000012';
            DECLARE @queue bigint=(SELECT q.queue_entry_id FROM dbo.anchorage_queue_entry q JOIN dbo.arrival_appointment a ON a.appointment_id=q.appointment_id
                WHERE a.appointment_no='PQ40000000000040008000000000000002'),
                @actor bigint=(SELECT user_id FROM dbo.user_account WHERE username='demo.dispatcher'),
                @at datetime2(0)=DATEADD(day,2,SYSUTCDATETIME());
            EXEC dbo.usp_set_anchorage_estimate @queue,@at,0,N'人工预计应保留',@actor;
            """);
        var previousAudit = await db.QueryAsync<string>("SELECT CONCAT(audit_id,':',COALESCE(old_data_json,''),':',COALESCE(new_data_json,'')) FROM dbo.audit_log ORDER BY audit_id");
        var human = await Snapshot(db, 12);
        var estimate = await db.QuerySingleAsync<DateTime>("SELECT manual_estimated_berthing_at FROM dbo.anchorage_queue_entry q JOIN dbo.arrival_appointment a ON a.appointment_id=q.appointment_id WHERE a.appointment_no='PQ40000000000040008000000000000002'");
        await fixture.ApplyScriptAsync("18_refine_anchorage_scenarios.sql");
        var facts = (await db.QueryAsync<(DateTime Submitted, DateTime Reviewed, DateTime Entered)>("""
            SELECT a.submitted_at,a.reviewed_at,q.entered_anchorage_at FROM dbo.arrival_appointment a
            JOIN dbo.v_anchorage_queue q ON q.appointment_id=a.appointment_id
            WHERE a.appointment_no LIKE 'PQ40000000000040008000%' AND RIGHT(a.appointment_no,12)<='000000000011'
            """)).ToArray();
        Assert.Equal(11, facts.Length);
        Assert.All(facts, f => Assert.True(f.Submitted < f.Reviewed && f.Reviewed < f.Entered));
        Assert.Equal(11, facts.Select(f => f.Entered).Distinct().Count());
        Assert.Equal(human, await Snapshot(db, 12));
        Assert.Equal(estimate, await db.QuerySingleAsync<DateTime>("SELECT manual_estimated_berthing_at FROM dbo.anchorage_queue_entry q JOIN dbo.arrival_appointment a ON a.appointment_id=q.appointment_id WHERE a.appointment_no='PQ40000000000040008000000000000002'"));
        Assert.Equal(22, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log WHERE JSON_VALUE(new_data_json,'$.seed_revision')='stage4-chronology-v1'"));
        Assert.True(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.anchorage_queue_entry q JOIN dbo.arrival_appointment a ON a.appointment_id=q.appointment_id WHERE a.appointment_no LIKE 'PQ40000000000040008000%' AND q.queue_status_code='ASSIGNED'") >= 1);
        var allAudit = (await db.QueryAsync<string>("SELECT CONCAT(audit_id,':',COALESCE(old_data_json,''),':',COALESCE(new_data_json,'')) FROM dbo.audit_log ORDER BY audit_id")).ToArray();
        Assert.Equal(previousAudit, allAudit.Take(previousAudit.Count()));
        var count = allAudit.Length;
        var first = await Snapshot(db, 1);
        await fixture.ApplyScriptAsync("17_seed_anchorage_scenarios.sql");
        await fixture.ApplyScriptAsync("18_refine_anchorage_scenarios.sql");
        Assert.Equal(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.audit_log"));
        Assert.Equal(first, await Snapshot(db, 1));
        Assert.Equal("内锚地", await db.ExecuteScalarAsync<string>("SELECT anchorage_name FROM dbo.anchorage a JOIN dbo.port p ON p.port_id=a.port_id WHERE p.port_code='SH-YS' AND anchorage_code='ANCH-INNER-A'"));
    }

    private static Task<string> Snapshot(SqlConnection db, int number) => db.QuerySingleAsync<string>("""
        SELECT (SELECT a.*,q.entered_anchorage_at,q.left_anchorage_at,q.queue_position,q.queue_status_code
            FROM dbo.arrival_appointment a JOIN dbo.v_anchorage_queue q ON q.appointment_id=a.appointment_id
            WHERE a.appointment_no=CONCAT('PQ40000000000040008000',RIGHT(CONCAT('000000000000',@number),12))
            FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)
        """, new { number });
}
