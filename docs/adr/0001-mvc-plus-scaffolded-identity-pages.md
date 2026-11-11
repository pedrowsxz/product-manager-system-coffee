# ADR 0001: MVC for the app, scaffolded Identity Razor Pages for accounts

**Status:** accepted

## Context
The brief allows any ASP.NET front end and suggests the scaffolded Identity UI. The job's stack lists ASP.NET Core MVC.

## Decision
- The product pages and the Admin area are **MVC** (controllers + Razor views, attribute routes in pt-BR: `/Produtos`, `/Admin/Equipe`).
- Account flows use the **Identity UI Razor Pages**, scaffolded with the official `dotnet-aspnet-codegenerator identity` (10.0.3) into `Areas/Identity`. Only the pages we use were scaffolded, then translated to pt-BR and given the app layout.
- Every other page the Identity UI library ships (2FA, external logins, personal data, `AccessDenied`, ...) has its route removed by a page route convention (`IdentityPages.Used`), so it returns 404 to signed-in users. Anonymous visitors are sent to the login page first, like for any unknown URL under the fallback policy. Tests request each one.

## Consequences
- Two UI styles in one app. This is normal in ASP.NET Core: both share the same middleware, filters, layout and tag helpers.
- The account logic comes from Microsoft's own pages, adapted; it isn't hand-rolled. We still own the scaffolded code, so library updates don't change it automatically.
- `AccessDenied` isn't used: a 403 is returned as a status code and rendered by the friendly error page, which is also easy to assert in tests.
