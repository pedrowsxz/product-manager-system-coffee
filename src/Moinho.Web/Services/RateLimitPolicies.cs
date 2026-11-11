namespace Moinho.Web.Services;

public static class RateLimitPolicies
{
    // Applied to every endpoint whose POST sends an email. GETs are never limited.
    public const string Email = "email";

    // Admin staff edits (an email change sends a confirmation email). Looser than Email, so an
    // admin can fix several records in a row.
    public const string AdminEdit = "admin-edit";

    // The demo sign-in buttons.
    public const string DemoLogin = "demo-login";
}
