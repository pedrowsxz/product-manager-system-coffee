using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ConfirmEmailModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public bool Succeeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        if (userId is null || code is null)
        {
            return RedirectToPage("./Login");
        }

        var user = await userManager.FindByIdAsync(userId);
        var token = AccountEmails.Decode(code);
        if (user is not null && token is not null)
        {
            Succeeded = (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
        }
        return Page();
    }
}
