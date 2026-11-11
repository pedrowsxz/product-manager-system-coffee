using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moinho.Web.Models;

namespace Moinho.Web.Areas.Identity.Pages.Account.Manage;

public class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public string Email { get; private set; } = "";

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Informe seu nome.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
        [Display(Name = "Nome completo")]
        public string FullName { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        Email = user.Email ?? "";
        Input.FullName = user.FullName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }
        if (user.IsProtectedDemo)
        {
            StatusMessage = "Erro: contas de demonstração não podem ser alteradas.";
            return RedirectToPage();
        }
        if (!ModelState.IsValid)
        {
            Email = user.Email ?? "";
            return Page();
        }

        user.FullName = Input.FullName.Trim();
        await userManager.UpdateAsync(user);
        StatusMessage = "Perfil atualizado.";
        return RedirectToPage();
    }
}
