# ADR 0006: Protected demo accounts, one-click demo login and a nightly reset

**Status:** accepted

## Context
Recruiters should try both roles in seconds, without registering. But a public Admin account can be used to break the demo for everyone.

## Decision
- The seeder creates `admin.demo@moinho.test` (Admin) and `equipe.demo@moinho.test` (Staff) with `IsProtectedDemo = true` and **no password**. The only way in is the login page's demo buttons: an antiforgery-protected, rate-limited POST, available only when `Demo:Enabled`.
- Protected demo accounts can't be deleted, demoted or deactivated, and their email or password can't be changed. This is enforced in `StaffService`, the Manage pages (ChangePassword, Email), ForgotPassword, ResetPassword and ConfirmEmailChange.
- The real admin from `Seed:AdminEmail` gets the same protection in the Admin area: it can't be deleted, deactivated, or have its role or email changed. Email matters as much as role, because whoever controls the address can reset the password. So the public demo admin can't lock the owner out.
- No protected account's role can change, not even upward. Otherwise the demo admin could promote `equipe.demo`, and the "Equipe" button would sign every visitor in as Admin.
- Invitations share the per-IP email rate limit. Staff edits, which can send a confirmation email, have a looser limit of their own (30 per 15 min). Both protect the provider's daily quota.
- **Nightly reset** (`Demo:NightlyReset`): a `BackgroundService` waits until 04:00 São Paulo time (read from `TimeProvider`), deletes every row in foreign-key order with `ExecuteDeleteAsync`, and reseeds.
- It deletes rows instead of dropping the database: `DROP DATABASE` needs the `dbcreator` permission and fails while connections are open, which matters when SQL Server is shared (DEPLOY layout B).

## Consequences
- Self-registered accounts, products added by visitors and error logs disappear every night, and everyone is signed out. The footer says "dados de demonstração são restaurados diariamente".
- The seed is deterministic (`Randomizer.Seed = new Random(2468)`), so the demo looks the same after each reset. A test runs the real service with a `FakeTimeProvider` and checks the exact seed counts.
- Between resets a visitor can still change the seeded staff and products. That's acceptable for a demo.
