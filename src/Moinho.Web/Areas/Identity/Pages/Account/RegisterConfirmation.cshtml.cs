using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Moinho.Web.Areas.Identity.Pages.Account;

// A static page on purpose: it must look the same for new and already-registered emails.
[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    public bool EmailFailed { get; private set; }

    public void OnGet()
    {
        EmailFailed = TempData["EmailFailed"] is true;
    }
}
