using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Areas.Admin.Controllers;

[Route("Admin/Logs")]
public class LogsController(AppDbContext db, IOptions<DemoOptions> demo) : AdminControllerBase
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] LogListQuery query)
    {
        var logs = db.ErrorLogs.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Level))
        {
            logs = logs.Where(l => l.Level == query.Level);
        }
        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            logs = logs.Where(l => l.TraceId == query.TraceId.Trim());
        }
        // The filter dates are São Paulo days; the column is UTC.
        if (query.From is { } from)
        {
            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), SaoPaulo);
            logs = logs.Where(l => l.TimeStamp >= fromUtc);
        }
        if (query.To is { } to)
        {
            var toUtc = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), SaoPaulo);
            logs = logs.Where(l => l.TimeStamp < toUtc);
        }

        var total = await logs.CountAsync();
        var page = Math.Clamp(query.Page, 1, 10_000); // a huge page number would overflow Skip()
        var items = await logs
            .OrderByDescending(l => l.TimeStamp).ThenByDescending(l => l.Id)
            .Skip((page - 1) * LogListQuery.PageSize)
            .Take(LogListQuery.PageSize)
            .ToListAsync();

        ViewBag.Query = query;
        ViewBag.SimulationEnabled = demo.Value.EnableErrorSimulation;
        return View(new PagedList<ErrorLog>(items, page, LogListQuery.PageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var log = await db.ErrorLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log is null)
        {
            return NotFound();
        }

        ViewBag.Properties = PrettyProperties(log.LogEvent);
        return View(log);
    }

    // Demo only: lets a visitor see the whole pipeline (friendly page with TraceId -> row here).
    [HttpPost("SimularErro")]
    public IActionResult SimulateError()
    {
        if (!demo.Value.EnableErrorSimulation)
        {
            return NotFound();
        }

        throw new InvalidOperationException("Erro simulado pelo botão \"Simular erro\" (demonstração).");
    }

    private static string? PrettyProperties(string? logEvent)
    {
        if (string.IsNullOrEmpty(logEvent))
        {
            return null;
        }

        using var json = JsonDocument.Parse(logEvent);
        return json.RootElement.TryGetProperty("Properties", out var properties)
            ? JsonSerializer.Serialize(properties, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            : logEvent;
    }
}
