using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Email)]
public class ResendEmailConfirmationModel(UserManager<ApplicationUser> userManager, AccountEmails emails) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; private set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Informe o email.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is { EmailConfirmed: false, IsProtectedDemo: false })
        {
            await emails.SendConfirmationAsync(user);
        }

        // Same message whether or not the account exists.
        Sent = true;
        return Page();
    }
}
