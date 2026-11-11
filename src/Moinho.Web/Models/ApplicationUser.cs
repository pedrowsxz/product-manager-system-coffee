using Microsoft.AspNetCore.Identity;

namespace Moinho.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    // Seeded demo accounts that recruiters sign into. They can't be deleted, demoted,
    // deactivated, or have their email/password changed.
    public bool IsProtectedDemo { get; set; }
}
