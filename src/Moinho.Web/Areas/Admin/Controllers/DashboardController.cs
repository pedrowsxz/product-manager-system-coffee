using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moinho.Web.Data;
using Moinho.Web.Services;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Areas.Admin.Controllers;

[Route("Admin")]
public class DashboardController(AppDbContext db, TimeProvider time) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var now = time.GetUtcNow();
        var since = now.UtcDateTime.AddHours(-24);

        var model = new DashboardViewModel(
            ActiveProducts: await db.Products.CountAsync(p => p.IsActive),
            InactiveProducts: await db.Products.CountAsync(p => !p.IsActive),
            ActiveStaff: await db.Users.CountAsync(u => u.EmailConfirmed && (u.LockoutEnd == null || u.LockoutEnd <= now)),
            UnconfirmedStaff: await db.Users.CountAsync(u => !u.EmailConfirmed),
            LockedStaff: await db.Users.CountAsync(u => u.LockoutEnd > now && u.LockoutEnd != StaffService.InactiveLockoutEnd),
            InactiveStaff: await db.Users.CountAsync(u => u.LockoutEnd == StaffService.InactiveLockoutEnd),
            ErrorsLast24h: await db.ErrorLogs.CountAsync(l => l.TimeStamp >= since));

        return View(model);
    }
}
