using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Moinho.Web.Models;

namespace Moinho.Web.Services;

// Builds the token links and sends the account emails. Used by the Identity pages and StaffService.
// A delivery failure is logged at Error level (so it reaches the ErrorLogs table) and returned as
// "false", so the caller can show a friendly message instead of an error page.
public class AccountEmails(
    IEmailSender sender,
    UserManager<ApplicationUser> userManager,
    LinkGenerator links,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AccountEmails> logger)
{
    public async Task<bool> SendConfirmationAsync(ApplicationUser user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = PageLink("/Account/ConfirmEmail", new { area = "Identity", userId = user.Id, code = Encode(token) });
        return await SendAsync(user, user.Email!, EmailTemplates.Confirmation(user.FullName, link), "confirmação de cadastro");
    }

    public async Task<bool> SendEmailChangeAsync(ApplicationUser user, string newEmail)
    {
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var link = PageLink("/Account/ConfirmEmailChange", new { area = "Identity", userId = user.Id, email = newEmail, code = Encode(token) });
        return await SendAsync(user, newEmail, EmailTemplates.EmailChange(user.FullName, link), "troca de email");
    }

    public async Task<bool> SendPasswordResetAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = PageLink("/Account/ResetPassword", new { area = "Identity", code = Encode(token) });
        return await SendAsync(user, user.Email!, EmailTemplates.PasswordReset(user.FullName, link), "redefinição de senha");
    }

    // An invitation is a password-reset token for a user that has no password yet.
    public async Task<bool> SendInvitationAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = PageLink("/Account/ResetPassword", new { area = "Identity", code = Encode(token), email = user.Email, invite = true });
        return await SendAsync(user, user.Email!, EmailTemplates.Invitation(user.FullName, link), "convite");
    }

    // Absolute URL built from the current request's scheme and host. Behind a proxy those come from
    // the X-Forwarded-* headers (see UseForwardedHeaders in Program.cs).
    private string PageLink(string page, object values) =>
        links.GetUriByPage(httpContextAccessor.HttpContext!, page, handler: null, values)
        ?? throw new InvalidOperationException($"Não foi possível gerar o link para {page}.");

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private async Task<bool> SendAsync(ApplicationUser user, string to, EmailContent email, string kind)
    {
        try
        {
            await sender.SendEmailAsync(to, email.Subject, email.Html);
            logger.LogInformation("Email de {EmailKind} enviado para o usuário {UserId}", kind, user.Id);
            return true;
        }
        catch (Exception ex)
        {
            // The link contains a token, so only the kind of email and the user id are logged.
            logger.LogError(ex, "Falha ao enviar email de {EmailKind} para o usuário {UserId}", kind, user.Id);
            return false;
        }
    }
}
