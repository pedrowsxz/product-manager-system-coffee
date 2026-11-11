# ADR 0006: Protected demo accounts, one-click demo login and a nightly reset

**Status:** accepted

## Context
Recruiters should try both roles in seconds, without registering. But a public Admin account can be used to break the demo for everyone.

## Decision
- The seeder creates `admin.demo@moinho.app` (Admin) and `equipe.demo@moinho.app` (Staff) with `IsProtectedDemo = true` and **no password**. The only way in is the login page's demo buttons: an antiforgery-protected, rate-limited POST, available only when `Demo:Enabled`.
- Protected demo accounts can't be deleted, demoted or deactivated, and their email or password can't be changed. This is enforced in `StaffService`, the Manage pages (ChangePassword, Email), ForgotPassword, ResetPassword and ConfirmEmailChange.
- The real admin from `Seed:AdminEmail` is also protected from deletion, demotion and deactivation, so the public demo admin can't lock the owner out.
- Admin actions that send email (invite, email change) share the per-IP email rate limit, which protects the provider's daily quota.
- **Nightly reset** (`Demo:NightlyReset`): a `BackgroundService` waits until 04:00 São Paulo time (read from `TimeProvider`), deletes every row in foreign-key order with `ExecuteDeleteAsync`, and reseeds.
- It deletes rows instead of dropping the database: `DROP DATABASE` needs the `dbcreator` permission and fails while connections are open, which matters when SQL Server is shared (DEPLOY layout B).

## Consequences
- Self-registered accounts, products added by visitors and error logs disappear every night, and everyone is signed out. The footer says "dados de demonstração são restaurados diariamente".
- The seed is deterministic (`Randomizer.Seed = new Random(2468)`), so the demo looks the same after each reset. A test runs the real service with a `FakeTimeProvider` and checks the exact seed counts.
- Between resets a visitor can still change the seeded staff and products. That's acceptable for a demo.
