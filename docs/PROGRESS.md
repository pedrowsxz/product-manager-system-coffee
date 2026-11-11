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
