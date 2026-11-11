# ADR 0005: Errors to the database with Serilog, table mapped in EF

**Status:** accepted

## Context
The brief asks for logging with errors saved to the database. The table must exist before the sink writes, and `EnsureCreated` owns the schema (ADR 0002).

## Decision
- Serilog writes everything (Information and above) to the **console**, and **Error and above** to SQL Server through `Serilog.Sinks.MSSqlServer`.
- The database sink only accepts events from our own code (`Moinho.*`). The exception-handler middleware and request logging report the same failure again, and this filter keeps it to **one row per failure**.
- The table is the `ErrorLog` EF entity, so `EnsureCreated` creates it. The sink runs with `AutoCreateSqlTable = false`, and its column options (`ErrorLogSink.Columns()`) match the entity: Id, Message, MessageTemplate, Level nvarchar(16), TimeStamp datetime2 (UTC), Exception, LogEvent (JSON), TraceId. These were checked against the sink's source.
- A mismatch would fail silently, so `SelfLog` is enabled in Development and a test checks that a real row arrives.
- `/Erro` logs the exception with the path and user id, never form values or tokens. The page shows the **TraceId**, the same value stored in the row, and admins can search for it on the Logs page.
- The sink batches writes. The host owns and disposes its logger, which flushes the batch on shutdown, and `Log.CloseAndFlushAsync()` covers the bootstrap logger.

## Consequences
- Logs are queryable with SQL and visible in the app without extra infrastructure.
- Errors share the app's database. In a larger system they'd go to a log service (Seq, Application Insights, Loki) instead.
- The sink 9.0.3 is used instead of 10.0.0 to keep a single `Microsoft.Data.SqlClient` major version with EF Core 10.
