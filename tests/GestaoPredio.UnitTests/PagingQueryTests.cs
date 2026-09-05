using recepcaototem.Features.Common;

namespace GestaoPredio.UnitTests;

public sealed class PagingQueryTests
{
    [Fact]
    public void Defaults_are_stable()
    {
        Assert.True(PagingQuery.TryCreate(null, null, null, null, out var query, out var error));
        Assert.Null(error);
        Assert.Equal(new PagingQuery(1, 20, "all", null), query);
    }

    [Theory]
    [InlineData(0, 20, "all", "INVALID_PAGE")]
    [InlineData(1, 0, "all", "INVALID_PAGE_SIZE")]
    [InlineData(1, 101, "all", "INVALID_PAGE_SIZE")]
    [InlineData(1, 20, "deleted", "INVALID_STATUS")]
    public void Invalid_query_values_are_not_silently_corrected(int page, int size, string status, string code)
    {
        Assert.False(PagingQuery.TryCreate(page, size, status, null, out var query, out var error));
        Assert.Null(query);
        Assert.Equal(code, error!.Code);
    }

    [Fact]
    public void Search_is_trimmed_collapsed_and_normalized()
    {
        Assert.True(PagingQuery.TryCreate(2, 100, " ACTIVE ", "  Ána   Silva ", out var query, out _));
        Assert.Equal(new PagingQuery(2, 100, "active", "ANA SILVA"), query);
    }

    [Fact]
    public void Empty_search_is_absent_and_oversized_search_is_rejected()
    {
        Assert.True(PagingQuery.TryCreate(1, 20, "all", " \t ", out var empty, out _));
        Assert.Null(empty!.Search);
        Assert.False(PagingQuery.TryCreate(1, 20, "all", new string('a', 101), out _, out var error));
        Assert.Equal("INVALID_SEARCH", error!.Code);
    }
}
