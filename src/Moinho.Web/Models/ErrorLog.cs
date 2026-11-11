namespace Moinho.Web.Models;

// One row written by Serilog.Sinks.MSSqlServer. The property names and types mirror the
// sink's standard columns exactly (see AppDbContext), because the sink bulk-inserts by
// column name and a mismatch fails silently.
public class ErrorLog
{
    public int Id { get; set; }
    public string? Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Level { get; set; }
    public DateTime TimeStamp { get; set; }
    public string? Exception { get; set; }
    public string? LogEvent { get; set; }
    public string? TraceId { get; set; }
}
