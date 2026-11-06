using System.ComponentModel.DataAnnotations;
using PortQueue.Application.Appointments;

namespace PortQueue.Api.Appointments;

/// <summary>HTTP 查询参数校验与归一化；错误在访问数据库前返回。</summary>
public sealed class AppointmentQueryRequest : IValidatableObject
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public string? Keyword { get; init; }

    public AppointmentQuery ToQuery() => new(Page, PageSize,
        NullIfBlank(Status)?.ToUpperInvariant(), NullIfBlank(Keyword));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page is < 1 or > 1_000_000)
            yield return new ValidationResult("页码须为 1 至 1000000 的整数。", [nameof(Page)]);
        if (PageSize is < 1 or > 100)
            yield return new ValidationResult("每页条数须为 1 至 100 的整数。", [nameof(PageSize)]);
        var query = ToQuery();
        if (query.Keyword?.Length > 100)
            yield return new ValidationResult("关键词最多 100 个字符。", [nameof(Keyword)]);
        if (query.Status is not (null or "DRAFT" or "PENDING" or "APPROVED" or "REJECTED" or "CANCELLED" or "COMPLETED"))
            yield return new ValidationResult("预约状态不在允许范围内。", [nameof(Status)]);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
