using System.Data;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.MSSqlServer;

namespace Moinho.Web.Data;

public static class ErrorLogSink
{
    public const string TableName = "ErrorLogs";

    // Adds the database sink. Only Error and above, and only from our own code ("Moinho.*"):
    // framework components (exception middleware, request logging) report the same failure again,
    // so this keeps exactly one row per failure. Everything still goes to the console.
    public static LoggerConfiguration WriteErrorsToDatabase(this LoggerConfiguration logger, string connectionString) =>
        logger.WriteTo.Logger(errors => errors
            .Filter.ByIncludingOnly(Matching.FromSource("Moinho"))
            .WriteTo.MSSqlServer(
                connectionString,
                new MSSqlServerSinkOptions
                {
                    TableName = TableName,
                    // EnsureCreated already created the table from the ErrorLog entity.
                    AutoCreateSqlTable = false,
                },
                columnOptions: Columns(),
                restrictedToMinimumLevel: LogEventLevel.Error));

    // Must match the ErrorLog entity (see AppDbContext). The sink writes by column name and a
    // mismatch fails silently, so SelfLog is turned on in Development to surface it.
    public static ColumnOptions Columns()
    {
        var columns = new ColumnOptions();
        columns.Store.Remove(StandardColumn.Properties); // XML; LogEvent below has the same data as JSON
        columns.Store.Add(StandardColumn.LogEvent);
        columns.Store.Add(StandardColumn.TraceId);
        columns.TimeStamp.DataType = SqlDbType.DateTime2; // EF's default type for DateTime
        columns.TimeStamp.ConvertToUtc = true;
        return columns;
    }
}
