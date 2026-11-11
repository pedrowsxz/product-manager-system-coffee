# ADR 0004: Admin-created staff get an invitation, not an admin-chosen password

**Status:** accepted

## Context
Admins must be able to add staff. If the admin typed a password, the admin would know someone else's credentials and would have to send them over an insecure channel.

## Decision
- `StaffService.InviteAsync` creates the user **without a password** and with `EmailConfirmed = true` (the admin vouches for the address), then emails an **invitation** link.
- The link is a password-reset token opened on the scaffolded `ResetPassword` page with `invite=true`, which only changes the wording ("Definir sua senha") and pre-fills the email. No new page or token type.
- The list shows "Convite pendente" until the person sets a password.

## Consequences
- The admin never chooses or sees anyone's password.
- Reset tokens live 2 h, so an invitation expires in 2 h. The email says so and points to "Esqueci minha senha", which works because the address is already confirmed. A "resend invitation" button would be the next step.
- An admin changing someone's email sets `EmailConfirmed = false` and sends a normal confirmation link. The person can't sign in until they confirm the new address.
