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

## Stage 2: Email and account flows ✅

- `SmtpEmailSender` (MailKit 5, implements the non-generic `IEmailSender` the scaffolded pages use) with a plain-text alternative derived from the HTML.
  - MailKit 5 changed `SmtpClient.SendAsync` to take a `SendRequest`; checked against the package's XML docs.
- `EmailTemplates` (pt-BR: confirmation, email change, reset, invitation) and `AccountEmails`, which builds the token links with `LinkGenerator` and logs failures at Error without the link or token.
- Identity options: confirmed account required, lockout 5 tries / 10 min, unique email, pt-BR `IdentityErrorDescriber`.
  - A second Data Protection token provider gives confirmation and email-change links 24 h; reset and invitation links keep 2 h.
- `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`.
- 403 is returned as a status code (friendly page) instead of a redirect to `AccessDenied`.
- Forwarded headers (For/Proto/Host); trusted networks come from `ForwardedHeaders:KnownNetworks`.
- Rate limiter policy `email` (POST only, per IP, 5 per 15 min by default) on Register, ForgotPassword, ResendEmailConfirmation and Manage/Email.
- Data Protection keys persisted to `DataProtection:KeysPath` (a Docker volume).
- Identity route audit: a page route convention removes every Identity UI page not in `IdentityPages.Used`.
- Pages rewritten in pt-BR with these behaviour changes:
  - Register answers the same for an already-registered email and sends nothing, so it can't be used to discover accounts.
  - RegisterConfirmation is a static page (the scaffold showed the confirmation link and returned 404 for unknown emails).
  - The 2FA, external-login and phone-number code paths were removed.
  - Protected demo accounts: ChangePassword, Manage/Email, ForgotPassword, ResetPassword and ConfirmEmailChange all refuse them.
- Tests (35 total): register / confirm / login, refused before confirmation, duplicate register, forgot / reset (the old password stops working), same response for unknown emails, change password, demo protection, email change, email failure, forwarded host in links, unused routes return 404, rate limit.
- Checked by hand on the Docker stack: register → Mailpit → confirm → login; forgot → Mailpit → reset; no token in the container logs.

## Stage 3: Products ✅

- `ProductsController` (`RequireStaff`) with list, details, create, edit and delete (a GET confirmation page, then a POST). The routes are pt-BR (`/Produtos`, `/Produtos/Novo`, `/Produtos/{id}/Editar`, ...).
- The list searches name or SKU, filters by status, sorts by name, price or date added (column headers), and pages 20 at a time. All of it lives in the query string.
- `ProductFormViewModel` binds the form. It has no `Id`, `DateAdded` or `UpdatedAt`, so those can't be over-posted.
- **pt-BR decimals (deviation from the spec's proposed fix):** Price binds as a string, checked by a pt-BR `[RegularExpression]` and parsed with the pt-BR culture.
  - The same regex becomes jquery-validation's client rule through unobtrusive validation, so no JS override is needed.
  - The input is `type="text" inputmode="decimal"`.
  - "12.50" gets a clear message instead of being saved as 1250.
- Duplicate SKU: the unique index rejects it and `ProductService` turns SqlException 2627/2601 into the field error "SKU já cadastrado.". There's no pre-check, so there's one path that also covers races.
- EF's own Error-level logs for failed commands (`SaveChangesFailed`, `CommandError`) are ignored. Each failure already surfaces as an exception that is handled or logged once.
- `PagedList<T>`, plus the `_Pager` and `_Flash` partials, shared with the Admin pages later.
- Tests (51 total): the pt-BR price cases, rejected formats, price ≤ 0, negative stock, uppercase SKU, duplicate SKU (form and database), DateAdded over-posting on create and edit, delete, and list search/filter/sort/paging.

## Stage 4: Admin area ✅

- `AdminControllerBase` (`[Area("Admin")]` + `[Authorize(Policy = RequireAdmin)]`), the only base class. `Policies` constants replace the string literals.
- **Painel** (`/Admin`): active/inactive products, staff by status (active, unconfirmed, locked, inactive) and errors in the last 24 h. Each tile links to the filtered list.
- **Equipe** (`/Admin/Equipe`): search by name or email, filter by role and status, paging. Invite (create), edit and delete (GET confirmation, then POST).
- `StaffService` (CORE_LOGIC = implement): every write goes through `UserManager`.
  - Invitation: the account is created without a password and with `EmailConfirmed = true`, and an invite email links to ResetPassword (`invite=true`).
  - Inactive = `LockoutEnd = DateTimeOffset.MaxValue`. Changing a user's email sets `EmailConfirmed = false` and sends a confirmation email.
  - Every update calls `UpdateSecurityStampAsync`.
  - Rules: no self-demote, self-deactivate or self-delete; protected accounts can't be demoted, deactivated or deleted, and demo accounts' emails can't be changed; the last active admin is checked inside a **serializable transaction** together with the change.
  - **Addition (kickoff fix 2):** the real admin from `Seed:AdminEmail` is protected like the demo accounts. Invite and edit POSTs share the email rate limit, because the demo Admin account is public.
  - `IdentityResult`s that can only fail because of a bug throw. The transaction rolls back and the error page logs it.
- Badges: Ativo, Não confirmado, Bloqueado (temporary lockout), Inativo (deactivated), Convite pendente (no password yet), Demo.
- Tests (73 total):
  - reflection over every Admin-area controller (base class, policy, area, no `[AllowAnonymous]`);
  - Staff gets 403 and Admin gets 200; anonymous users are redirected; the nav link is hidden from Staff; antiforgery is enforced;
  - the self rules, protected accounts, the demo email, the invitation end to end, a duplicate invite;
  - a demoted admin is signed out on the next request; a deactivated user is signed out and gets the lockout page;
  - an email change needs confirmation; delete; list filters; the last admin (on a separate database).
- Checked on the Docker stack: Painel counts; invite → Mailpit → "Definir sua senha" → login; Staff gets 403 on `/Admin`.

## Stage 5: Logging ✅

- Serilog writes to the console (Information and above, with `Microsoft.AspNetCore` and `EntityFrameworkCore` at Warning, which also keeps request URLs with tokens out of the logs) and to `Serilog.Sinks.MSSqlServer` (9.0.3).
  - The database sink is a sub-logger that only takes **Error and above from `Moinho.*`**. The framework logs every unhandled exception again (ExceptionHandlerMiddleware, request logging), so this gives exactly one row per failure.
- The `ErrorLogs` table comes from the `ErrorLog` EF entity (EnsureCreated), and the sink runs with `AutoCreateSqlTable = false`.
  - Columns: Id, Message, MessageTemplate, Level nvarchar(16), TimeStamp datetime2 (UTC), Exception, LogEvent (JSON), TraceId.
  - The XML `Properties` column was dropped because `LogEvent` has the same data.
  - Checked against the sink 9.0.3 source and `INFORMATION_SCHEMA` on the running database. `SelfLog` is on in Development.
- The host owns its logger (`AddSerilog(..., preserveStaticLogger: true)`) and disposes it on shutdown, which flushes the batch. `Log.CloseAndFlushAsync()` covers the bootstrap logger.
- `/Erro` logs the exception at Error with the path and user id only, and shows the TraceId (`Activity.Current.TraceId`, the same value Serilog stores) plus a "Ver no log" link for admins.
- The Admin **Logs** page has level, date range (São Paulo days) and TraceId filters, paging, and a detail view showing the message, exception and pretty-printed properties.
- **Simular erro** (POST, `Demo:EnableErrorSimulation`, 404 when off).
- Startup retry now also catches EF's "transient failure" wrapper (parallel `CREATE DATABASE` calls can briefly fight over a lock on `model`).
- Tests (76 total):
  - simulate error → friendly 500 page without a stack trace → exactly one Error row with that TraceId, UserId in the properties, UTC timestamp, found on the list and details pages;
  - 404s and 403s write no row;
  - an email failure writes one row that contains no link, token or password.
- Checked on the Docker stack: simulate → TraceId on the page → the same row in SQL and on the Logs page.
- The dev Docker database had to be recreated because the ErrorLogs column types changed. That is exactly the EnsureCreated limitation from ADR 0002.
