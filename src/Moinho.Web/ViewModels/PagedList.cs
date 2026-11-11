namespace Moinho.Web.ViewModels;

public record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public Pager Pager => new(Page, TotalPages, TotalCount);
}

public record Pager(int Page, int TotalPages, int TotalCount);
