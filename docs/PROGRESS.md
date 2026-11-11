# Progress

## Stage 1: Foundation ✅

- Solution `Moinho.slnx` with `src/Moinho.Web` (MVC + Razor Pages) and `tests/Moinho.Tests` (xUnit v3).
- Packages pinned to the latest stable versions that support .NET 10 (Microsoft packages 10.0.15; the 11.0 line targets .NET 11 only).
  - Exception: `Serilog.Sinks.MSSqlServer` 9.0.3 instead of 10.0.0. The 10.x sink depends on `Microsoft.Data.SqlClient` 7, while EF Core 10 is built against SqlClient 6.1; 9.0.3 keeps one SqlClient major version.
- Identity pages scaffolded **with the official scaffolder** (`dotnet-aspnet-codegenerator identity` 10.0.3, run headlessly from a local tool manifest). The dev-time packages it needed (`CodeGeneration.Design`, `EF Core Design/Tools`) were removed afterwards.
  - Added `ConfirmEmailChange` (needed by Manage → Email). Removed `AccessDenied`: a 403 is shown by the friendly status page instead (see Stage 2).
- `ApplicationUser`, `Product`, `ErrorLog` (columns checked against the sink 9.0.3 source), `AppDbContext`.
- `DbInitializer`: `EnsureCreatedAsync` with retry, then the deterministic `Seeder` (roles, real admin from config, 2 protected demo accounts, 10 staff in mixed states, 60 coffee products from pt-BR templates).
- Fallback policy (authenticated), `RequireAdmin` / `RequireStaff`, global antiforgery, pt-BR request culture, `/Erro` + `/Erro/{code}` pages.
- Layout: sidebar back-office shell, signed-out card layout, one accent colour, light/dark following `prefers-color-scheme`.
- `Dockerfile` (multi-stage, non-root), `compose.yaml` (SQL Server with healthcheck, Mailpit, web). Host ports are overridable (`WEB_PORT`, `SQL_PORT`, ...) because this dev machine already runs another project on 8080/1433.
- Tests: one SQL Server Testcontainer per run (assembly fixture), one fresh database per test class created by the app's own startup (`EnsureCreated`), dropped on dispose.

Notes
- On this WSL machine SQL Server takes ~2 min to start and ~11 s per `CREATE DATABASE`, so a test run has a fixed ~2 min cost.
