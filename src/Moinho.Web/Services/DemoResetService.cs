using Moinho.Web.Data;

namespace Moinho.Web.Services;

// Runs DemoReset every day at 04:00 in São Paulo (Demo:NightlyReset). Reads the clock from
// TimeProvider so a test can move time forward instead of waiting until 4 a.m.
public class DemoResetService(IServiceScopeFactory scopes, TimeProvider time, ILogger<DemoResetService> logger) : BackgroundService
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset NextRun(DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, SaoPaulo).DateTime;
        var run = localNow.Date.AddHours(4);
        if (run <= localNow)
        {
            run = run.AddDays(1);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(run, SaoPaulo), TimeSpan.Zero);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            await Task.Delay(NextRun(now) - now, time, stoppingToken);

            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DemoReset>().ResetAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao restaurar os dados de demonstração");
            }
        }
    }
}
