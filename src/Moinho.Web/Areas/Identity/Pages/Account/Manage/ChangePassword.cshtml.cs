using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moinho.Web.Models;

namespace Moinho.Web.Areas.Identity.Pages.Account.Manage;

// "Reset their password" from the brief, read as: change the password while signed in.
public class ChangePasswordModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsProtectedDemo { get; private set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Informe a senha atual.")]
        [DataType(DataType.Password)]
        [Display(Name = "Senha atual")]
        public string OldPassword { get; set; } = "";

        [Required(ErrorMessage = "Informe a nova senha.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter entre {2} e {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nova senha")]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirme a nova senha")]
        [Compare(nameof(NewPassword), ErrorMessage = "As senhas não conferem.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        IsProtectedDemo = user.IsProtectedDemo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        IsProtectedDemo = user.IsProtectedDemo;
        if (user.IsProtectedDemo)
        {
            StatusMessage = "Erro: contas de demonstração não podem trocar a senha.";
            return RedirectToPage();
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Also changes the security stamp, so other sessions of this user are signed out.
        var result = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Re-issue this browser's cookie with the new stamp so it isn't signed out too.
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Senha alterada.";
        return RedirectToPage();
    }
}
