using GestaoPredio.Domain.Common;

namespace recepcaototem.Features.Common;

public sealed record PagingQuery(int Page, int PageSize, string Status, string? Search)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int MaximumSearchLength = 100;
    public const string All = "all";
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static bool TryCreate(int? page, int? pageSize, string? status, string? search,
        out PagingQuery? query, out ApiError? error)
    {
        var actualPage = page ?? DefaultPage;
        if (actualPage < 1)
            return Fail("INVALID_PAGE", "A página deve ser maior ou igual a 1.", out query, out error);
        var actualPageSize = pageSize ?? DefaultPageSize;
        if (actualPageSize is < 1 or > MaximumPageSize)
            return Fail("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100.", out query, out error);
        var actualStatus = string.IsNullOrWhiteSpace(status) ? All : status.Trim().ToLowerInvariant();
        if (actualStatus is not (All or Active or Inactive))
            return Fail("INVALID_STATUS", "O status informado é inválido.", out query, out error);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : TextNormalizer.Normalize(search);
        if (normalizedSearch?.Length == 0) normalizedSearch = null;
        if (normalizedSearch?.Length > MaximumSearchLength)
            return Fail("INVALID_SEARCH", "A busca deve possuir no máximo 100 caracteres.", out query, out error);

        query = new PagingQuery(actualPage, actualPageSize, actualStatus, normalizedSearch);
        error = null;
        return true;
    }

    private static bool Fail(string code, string message, out PagingQuery? query, out ApiError? error)
    {
        query = null;
        error = new ApiError(code, message);
        return false;
    }
}
