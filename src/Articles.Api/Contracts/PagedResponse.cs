using Articles.Domain.Common;

namespace Articles.Api.Contracts;

/// <summary>Wire shape for paged endpoints, kept separate from the domain's PagedResult.</summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage)
{
    public static PagedResponse<T> From(PagedResult<T> result) =>
        new(result.Items, result.PageNumber, result.PageSize, result.TotalCount, result.TotalPages, result.HasNextPage);
}
