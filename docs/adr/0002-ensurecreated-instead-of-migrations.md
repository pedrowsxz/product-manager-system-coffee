# ADR 0002: `EnsureCreated` instead of migrations (mandated by the brief)

**Status:** accepted (required by the brief)

## Context
The brief asks for the database and tables to be created automatically with `EnsureCreated`.

## Decision
- On startup, `DbInitializer` calls `Database.EnsureCreatedAsync()` (retrying while SQL Server starts), then runs the idempotent seeder.
- `EnsureCreated` creates **every** table from the EF model: Identity's, `Products` and `ErrorLogs`. That's why the Serilog sink runs with `AutoCreateSqlTable = false` (ADR 0005).
- No EF migrations exist in the repo. `EnsureCreated` builds the schema without a migrations history table, so mixing the two would break: `Migrate()` would later try to create tables that already exist.

## Consequences
- **A schema change needs a fresh database.** `EnsureCreated` does nothing if the database exists, so new columns never appear. Locally: `docker compose down -v`. On the server: drop the database (all data is seed data, and the demo resets nightly anyway).
- This happened during development: changing the `ErrorLogs` column types required recreating the dev database.
- Test databases come from the same `EnsureCreated` path as production.

## In a real system
Use **EF Core migrations**, committed to the repo and reviewed like code, and apply them as a deploy step, not at app startup: either a **migration bundle** (`dotnet ef migrations bundle`) or an **idempotent SQL script** (`dotnet ef migrations script --idempotent`) run by the pipeline or a DBA. Applying at startup races when several instances start and needs DDL rights for the app's login.
