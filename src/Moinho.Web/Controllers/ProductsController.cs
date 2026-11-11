using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moinho.Web.Data;

namespace Moinho.Web.Controllers;

[Authorize(Policy = "RequireStaff")]
[Route("Produtos")]
public class ProductsController(AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index() =>
        View(await db.Products.OrderBy(p => p.Name).Take(20).ToListAsync());
}
