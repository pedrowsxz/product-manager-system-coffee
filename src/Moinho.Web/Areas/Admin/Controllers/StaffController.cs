using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moinho.Web.Models;
using Moinho.Web.Services;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Areas.Admin.Controllers;

[Route("Admin/Equipe")]
public class StaffController(StaffService staff, UserManager<ApplicationUser> userManager) : AdminControllerBase
{
    private string CurrentUserId => userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] StaffListQuery query)
    {
        ViewBag.Query = query;
        return View(await staff.ListAsync(query));
    }

    [HttpGet("Novo")]
    public IActionResult Create() => View(new StaffCreateViewModel());

    // Sends an email to the typed address, so it shares the email rate limit (the demo admin is public).
    [HttpPost("Novo")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public async Task<IActionResult> Create(StaffCreateViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var result = await staff.InviteAsync(form);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? "", result.Error!);
            return View(form);
        }

        TempData[result.EmailFailed ? "Error" : "Message"] = result.EmailFailed
            ? $"Conta de {form.FullName} criada, mas o convite não pôde ser enviado. Peça para a pessoa usar \"Esqueci minha senha\"."
            : $"Convite enviado para {form.Email}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/Editar")]
    public async Task<IActionResult> Edit(string id)
    {
        var row = await staff.GetAsync(id);
        if (row is null)
        {
            return NotFound();
        }

        SetViewData(row);
        return View(new StaffEditViewModel
        {
            FullName = row.FullName,
            Email = row.Email,
            Role = row.Role,
            IsActive = row.Status != StaffStatus.Inactive,
        });
    }

    [HttpPost("{id}/Editar")]
    [EnableRateLimiting(RateLimitPolicies.AdminEdit)]
    public async Task<IActionResult> Edit(string id, StaffEditViewModel form)
    {
        var row = await staff.GetAsync(id);
        if (row is null)
        {
            return NotFound();
        }

        SetViewData(row);
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var result = await staff.UpdateAsync(CurrentUserId, id, form);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? "", result.Error!);
            return View(form);
        }

        TempData[result.EmailFailed ? "Error" : "Message"] = result.EmailFailed
            ? "Alterações salvas, mas o email de confirmação não pôde ser enviado."
            : "Alterações salvas.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/Excluir")]
    public async Task<IActionResult> Delete(string id)
    {
        var row = await staff.GetAsync(id);
        if (row is null)
        {
            return NotFound();
        }

        SetViewData(row);
        return View(row);
    }

    [HttpPost("{id}/Excluir")]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var result = await staff.DeleteAsync(CurrentUserId, id);
        if (result == StaffResult.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "Conta excluída." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private void SetViewData(StaffRow row)
    {
        ViewBag.Row = row;
        ViewBag.IsSelf = row.Id == CurrentUserId;
        ViewBag.IsProtected = staff.IsProtected(row);
    }
}
