using FluentValidation;
using FuelFlow.Domain.Common;

namespace FuelFlow.Application.Common;

/// <summary>The thing does not exist, or belongs to someone else. Both look the same to the caller (maps to 404).</summary>
public sealed class NotFoundException(string what) : Exception($"{what} was not found.");

public sealed class AddressInUseException(string message) : ConflictDomainException(message);

public sealed record PageRequest(int Page = 1, int PageSize = 20);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new([.. Items.Select(map)], Page, PageSize, TotalCount);
}

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public const int MaxPageSize = 100;

    // Keeps (page - 1) * pageSize far below int.MaxValue; a larger page number would overflow and break the query.
    public const int MaxPage = 1_000_000;

    public PageRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
