namespace Moinho.Web.ViewModels;

public class LogListQuery
{
    public const int PageSize = 20;

    public string? Level { get; set; }     // "Error", "Fatal" or empty for all
    public DateOnly? From { get; set; }    // São Paulo calendar days
    public DateOnly? To { get; set; }
    public string? TraceId { get; set; }
    public int Page { get; set; } = 1;
}
