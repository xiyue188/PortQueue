using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PortQueue.Application.Appointments;
using PortQueue.Infrastructure.Appointments;

namespace PortQueue.Api.Tests.Appointments;

/// <summary>通过 HTTP、真实 Dapper 和独立 SQL Server 库验证预约查询，不用 Mock 替代 SQL。</summary>
[Trait("Category", "Database")]
public sealed class AppointmentQueryTests(AppointmentDatabaseFixture fixture) : IClassFixture<AppointmentDatabaseFixture>
{
    private async Task<AppointmentPage> Query(string parameters = "")
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/appointments" + parameters);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppointmentPage>())!;
    }

    [Fact]
    public async Task ListsDatabaseRecordsIncludingUnreviewedAppointments()
    {
        var result = await Query();
        Assert.Equal(8, result.TotalCount);
        Assert.Equal(8, result.Items.Count);
        var pending = Assert.Single(result.Items, row => row.AppointmentNo == "PQ-DEMO-005");
        Assert.Null(pending.ReviewerName);
        Assert.Null(pending.ReviewedAt);
        Assert.Null(pending.ReviewComment);
        var approved = Assert.Single(result.Items, row => row.AppointmentNo == "PQ-DEMO-003");
        Assert.NotNull(approved.ReviewerName);
        Assert.NotNull(approved.ReviewedAt);
    }

    [Theory]
    [InlineData("DRAFT", 3)]
    [InlineData("PENDING", 1)]
    [InlineData("APPROVED", 2)]
    [InlineData("COMPLETED", 2)]
    [InlineData("REJECTED", 0)]
    [InlineData("CANCELLED", 0)]
    public async Task FiltersAllDefinedStatuses(string status, int count)
    {
        var result = await Query("?status=" + status);
        Assert.Equal(count, result.TotalCount);
        Assert.Equal(count, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(status, item.AppointmentStatusCode));
    }

    [Theory]
    [InlineData("PQ-DEMO-003", 1)]
    [InlineData("阶段二测试船", 3)]
    [InlineData("9999001", 3)]
    [InlineData("413999901", 3)]
    [InlineData("%", 1)]
    [InlineData("_", 1)]
    [InlineData("[", 1)]
    [InlineData("\\", 1)]
    [InlineData("' OR 1=1 --", 0)]
    [InlineData("not-found", 0)]
    public async Task SearchesIdentifiersAndTreatsWildcardsLiterally(string keyword, int count)
    {
        var result = await Query("?keyword=" + Uri.EscapeDataString(keyword));
        Assert.Equal(count, result.TotalCount);
        Assert.Equal(count, result.Items.Count);
    }

    [Fact]
    public async Task NormalizesWhitespaceAndCombinesFilters()
    {
        var result = await Query("?status=%20approved%20&keyword=%20PQ-DEMO-003%20");
        Assert.Equal("PQ-DEMO-003", Assert.Single(result.Items).AppointmentNo);
        Assert.Empty((await Query("?status=PENDING&keyword=PQ-DEMO-003")).Items);
        Assert.Equal(8, (await Query("?status=%20&keyword=%20")).TotalCount);
    }

    [Fact]
    public async Task PagesHaveStableOrderAndNoDuplicateRows()
    {
        var first = await Query("?keyword=S2-TIE&pageSize=1");
        var second = await Query("?keyword=S2-TIE&pageSize=1&page=2");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal("S2-TIE-B", Assert.Single(first.Items).AppointmentNo);
        Assert.Equal("S2-TIE-A", Assert.Single(second.Items).AppointmentNo);
        var ids = new List<string>();
        for (var page = 1; page <= 4; page++)
            ids.AddRange((await Query($"?pageSize=2&page={page}")).Items.Select(item => item.AppointmentId));
        Assert.Equal(8, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task OutOfRangePageKeepsTotalAndRequestedPage()
    {
        var page = await Query("?page=1000000&pageSize=100");
        Assert.Empty(page.Items);
        Assert.Equal(8, page.TotalCount);
        Assert.Equal(1_000_000, page.Page);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task PreservesBigintAsStringAndUtcOffset()
    {
        using var client = fixture.Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/appointments?keyword=S2-TIE-A"));
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.String, item.GetProperty("appointmentId").ValueKind);
        Assert.Equal("9007199254740993", item.GetProperty("appointmentId").GetString());
        var time = item.GetProperty("requestedArrivalAt").GetString()!;
        Assert.True(time.EndsWith("+00:00", StringComparison.Ordinal) || time.EndsWith('Z'));
        Assert.Equal(TimeSpan.Zero, item.GetProperty("requestedArrivalAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task DetailUsesExactBigintAndPreservesListScope()
    {
        using var client = fixture.Factory.CreateClient();
        var before = await Query("?pageSize=2");
        var item = (await client.GetFromJsonAsync<AppointmentListItem>("/api/appointments/9007199254740993"))!;
        Assert.Equal("9007199254740993", item.AppointmentId);
        Assert.Equal("S2-TIE-A", item.AppointmentNo);
        var after = await Query("?pageSize=2");
        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.Items, after.Items);
        using var missing = await client.GetAsync("/api/appointments/9223372036854775807");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DetailWorksWithExistingColumnPermissions()
    {
        await using var stream = typeof(SqlAppointmentQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.AppointmentDetail.sql")!;
        using var reader = new StreamReader(stream);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXECUTE AS USER='portqueue_probe';\n" + await reader.ReadToEndAsync() + "\nREVERT;", connection);
        command.Parameters.Add("@AppointmentId", System.Data.SqlDbType.BigInt).Value = 9007199254740993L;
        using var rows = await command.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal("S2-TIE-A", rows.GetString(rows.GetOrdinal("AppointmentNo")));
    }

    [Fact]
    public async Task CompleteQueryWorksWithExistingColumnPermissions()
    {
        await using var stream = typeof(SqlAppointmentQueries).Assembly.GetManifestResourceStream("PortQueue.Queries.Appointments.sql")!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXECUTE AS USER='portqueue_probe';\n" + sql + "\nREVERT;", connection);
        command.Parameters.Add("@Status", System.Data.SqlDbType.VarChar, 30).Value = DBNull.Value;
        command.Parameters.Add("@Pattern", System.Data.SqlDbType.NVarChar, 202).Value = DBNull.Value;
        command.Parameters.Add("@Offset", System.Data.SqlDbType.BigInt).Value = 0;
        command.Parameters.Add("@PageSize", System.Data.SqlDbType.Int).Value = 20;
        using var rows = await command.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal(8, rows.GetInt64(rows.GetOrdinal("TotalCount")));
    }
}
