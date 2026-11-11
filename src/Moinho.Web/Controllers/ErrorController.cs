using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Controllers;

// Anonymous and without antiforgery checks: these actions are re-executed for whatever request failed,
// including anonymous visitors and POSTs.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class ErrorController(ILogger<ErrorController> logger) : Controller
{
    [Route("Erro")]
    public IActionResult Error()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        var failure = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

        if (failure is not null)
        {
            // Path and user id only: never the query string, form values, passwords or tokens.
            logger.LogError(failure.Error, "Erro não tratado em {Path} (usuário {UserId})",
                failure.Path, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anônimo");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
        }

        return View(new ErrorViewModel(traceId));
    }

    // 403/404/429 etc. are expected outcomes, not errors, so they are not logged.
    [Route("Erro/{code:int}")]
    public IActionResult Status(int code) => View(code);
}
