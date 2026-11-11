using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Web.Controllers;

// The one-click buttons on the login page, so a recruiter can try both roles without registering.
// They only work with Demo:Enabled and only for the protected demo accounts.
[AllowAnonymous]
[Route("demo")]
public class DemoController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IOptions<DemoOptions> demo) : Controller
{
    [HttpPost("entrar/{account}")]
    [EnableRateLimiting(RateLimitPolicies.DemoLogin)]
    public async Task<IActionResult> SignIn(string account)
    {
        var email = account switch
        {
            "admin" => DemoAccounts.AdminEmail,
            "equipe" => DemoAccounts.StaffEmail,
            _ => null,
        };
        if (!demo.Value.Enabled || email is null)
        {
            return NotFound();
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is not { IsProtectedDemo: true })
        {
            return NotFound();
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Redirect(account == "admin" ? "/Admin" : "/Produtos");
    }
}
