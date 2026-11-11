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
public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    AccountEmails emails,
    TimeProvider time,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = "/";

    public class InputModel
    {
        [Required(ErrorMessage = "Informe seu nome.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
        [Display(Name = "Nome completo")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Informe o email.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Informe uma senha.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter entre {2} e {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Senha")]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirme a senha")]
        [Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Same answer whether or not the email is already registered, so this form can't be used
        // to find out who has an account. The owner of the address can use "Esqueci minha senha".
        if (await userManager.FindByEmailAsync(Input.Email) is not null)
        {
            logger.LogInformation("Cadastro ignorado: email já cadastrado");
            return RedirectToPage("./RegisterConfirmation");
        }

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            FullName = Input.FullName.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Self-registered users are always Staff. Only an admin can grant Admin.
        await userManager.AddToRoleAsync(user, Roles.Staff);
        logger.LogInformation("Novo usuário cadastrado: {UserId}", user.Id);

        if (!await emails.SendConfirmationAsync(user))
        {
            TempData["EmailFailed"] = true;
        }
        return RedirectToPage("./RegisterConfirmation");
    }
}
