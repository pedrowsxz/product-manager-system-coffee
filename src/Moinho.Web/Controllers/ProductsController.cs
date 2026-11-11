using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moinho.Web.Models;
using Moinho.Web.Services;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Controllers;

[Authorize(Policy = Policies.RequireStaff)]
[Route("Produtos")]
public class ProductsController(ProductService products) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductListQuery query)
    {
        ViewBag.Query = query;
        return View(await products.ListAsync(query));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var product = await products.FindAsync(id);
        return product is null ? NotFound() : View(product);
    }

    [HttpGet("Novo")]
    public IActionResult Create() => View(new ProductFormViewModel());

    [HttpPost("Novo")]
    public async Task<IActionResult> Create(ProductFormViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var product = await products.CreateAsync(form);
        if (product is null)
        {
            ModelState.AddModelError(nameof(form.Sku), ProductService.DuplicateSkuMessage);
            return View(form);
        }

        TempData["Message"] = $"Produto \"{product.Name}\" cadastrado.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    [HttpGet("{id:int}/Editar")]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        ViewBag.Product = product;
        return View(ProductFormViewModel.From(product));
    }

    [HttpPost("{id:int}/Editar")]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel form)
    {
        var product = await products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        ViewBag.Product = product;
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        if (!await products.UpdateAsync(product, form))
        {
            ModelState.AddModelError(nameof(form.Sku), ProductService.DuplicateSkuMessage);
            return View(form);
        }

        TempData["Message"] = "Alterações salvas.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:int}/Excluir")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await products.FindAsync(id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost("{id:int}/Excluir")]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        await products.DeleteAsync(product);
        TempData["Message"] = $"Produto \"{product.Name}\" excluído.";
        return RedirectToAction(nameof(Index));
    }
}
