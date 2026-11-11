using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Web.Areas.Identity.Pages.Account.Manage;

[EnableRateLimiting(RateLimitPolicies.Email)]
public class EmailModel(UserManager<ApplicationUser> userManager, AccountEmails emails) : PageModel
{
    public string Email { get; private set; } = "";

    public bool IsEmailConfirmed { get; private set; }

    public bool IsProtectedDemo { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Informe o novo email.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        [Display(Name = "Novo email")]
        public string NewEmail { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        Load(user);
        Input.NewEmail = user.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        if (user.IsProtectedDemo)
        {
            StatusMessage = "Erro: contas de demonstração não podem trocar o email.";
            return RedirectToPage();
        }
        if (!ModelState.IsValid)
        {
            Load(user);
            return Page();
        }
        if (string.Equals(Input.NewEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Seu email não mudou.";
            return RedirectToPage();
        }

        // The email only changes when the link sent to the new address is opened.
        StatusMessage = await emails.SendEmailChangeAsync(user, Input.NewEmail)
            ? "Enviamos um link de confirmação para o novo email. A troca vale depois que você clicar nele."
            : "Erro: não foi possível enviar o email agora. Tente novamente em alguns minutos.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendVerificationEmailAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        StatusMessage = await emails.SendConfirmationAsync(user)
            ? "Email de confirmação enviado."
            : "Erro: não foi possível enviar o email agora. Tente novamente em alguns minutos.";
        return RedirectToPage();
    }

    private void Load(ApplicationUser user)
    {
        Email = user.Email ?? "";
        IsEmailConfirmed = user.EmailConfirmed;
        IsProtectedDemo = user.IsProtectedDemo;
    }
}
