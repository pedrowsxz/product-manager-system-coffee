# ADR 0003: Security stamp validated on every request

**Status:** accepted

## Context
After sign-in, the user's roles live inside the auth cookie. Identity re-checks the cookie against the database (the **security stamp**) only every 30 minutes by default. So a demoted, deactivated or deleted user could keep using the Admin area for up to 30 minutes.

## Decision
- `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`: the stamp is compared on every request.
- Every admin change to a user's role, active status or email calls `UserManager.UpdateSecurityStampAsync` (password reset and change already do).
- When the stamps differ, the validator rejects the cookie and the user is signed out on their next request. They sign in again and get a cookie with their current roles.

## Consequences
- More work per authenticated request: the validator loads the user, rebuilds the principal (roles and claims) from the database and re-issues the cookie. That's a few queries plus a `Set-Cookie` each time, which is fine for a back-office with a handful of users.
- Changing your own password would sign out your current browser too, so `ChangePassword` calls `RefreshSignInAsync` to re-issue its cookie.
- The nightly demo reset deletes all users, so every session ends at 04:00 (ADR 0006).
- Covered by a test: an admin demotes another admin, and the victim's very next request to `/Admin` is redirected to the login page.
