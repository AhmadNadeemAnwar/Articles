using Articles.Domain.Common;

namespace Articles.Application.Common;

public static class PagedResultExtensions
{
    public static PagedResult<TOut> Map<TIn, TOut>(this PagedResult<TIn> source, Func<TIn, TOut> selector) =>
        new(source.Items.Select(selector).ToList(), source.PageNumber, source.PageSize, source.TotalCount);
}
