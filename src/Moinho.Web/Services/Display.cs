using System.Globalization;

namespace Moinho.Web.Services;

// Everything is stored in UTC; this is the single place that converts for display.
public static class Display
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static System.DateTime ToSaoPaulo(System.DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc), SaoPaulo);

    public static string DateTime(System.DateTime utc) => ToSaoPaulo(utc).ToString("dd/MM/yyyy HH:mm", PtBr);

    public static string Date(System.DateTime utc) => ToSaoPaulo(utc).ToString("dd/MM/yyyy", PtBr);

    public static string Money(decimal value) => value.ToString("C", PtBr);
}
