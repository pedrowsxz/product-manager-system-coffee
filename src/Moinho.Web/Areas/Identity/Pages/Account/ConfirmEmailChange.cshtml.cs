using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Moinho.Web.Models;

namespace Moinho.Web.Areas.Identity.Pages.Account;

// Anonymous because the link may be opened in a browser where the user isn't signed in;
// the token in the link is what authorizes the change.
[AllowAnonymous]
public class ConfirmEmailChangeModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    public bool Succeeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (userId is null || email is null || code is null)
        {
            return RedirectToPage("./Login");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null || user.IsProtectedDemo)
        {
            return Page();
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        if (!(await userManager.ChangeEmailAsync(user, email, token)).Succeeded)
        {
            return Page();
        }

        // Email and user name are the same thing in this app, so keep them in sync.
        if (!(await userManager.SetUserNameAsync(user, email)).Succeeded)
        {
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        Succeeded = true;
        return Page();
    }
}
