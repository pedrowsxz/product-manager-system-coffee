using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Web.Areas.Identity.Pages.Account;

// Used both for "forgot password" links and for staff invitations (a reset token for a user
// who has no password yet). "invite=true" only changes the wording.
[AllowAnonymous]
public class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool Invite { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Informe o email.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Informe a nova senha.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter entre {2} e {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nova senha")]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirme a senha")]
        [Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
        public string ConfirmPassword { get; set; } = "";

        [Required]
        public string Code { get; set; } = "";
    }

    public IActionResult OnGet(string? code = null, string? email = null)
    {
        if (code is null)
        {
            return RedirectToPage("./ForgotPassword");
        }

        Input = new InputModel { Code = code, Email = email ?? "" };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        var token = AccountEmails.Decode(Input.Code);
        if (user is null || user.IsProtectedDemo || token is null)
        {
            // Same answer as a wrong or expired link, so the form doesn't reveal which emails exist.
            ModelState.AddModelError(string.Empty, userManager.ErrorDescriber.InvalidToken().Description);
            return Page();
        }

        // Also changes the security stamp, which signs the user out everywhere else.
        var result = await userManager.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
        return Page();
    }
}
