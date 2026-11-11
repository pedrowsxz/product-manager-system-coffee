namespace Moinho.Web.Services;

public static class RateLimitPolicies
{
    // Applied to every endpoint whose POST sends an email. GETs are never limited.
    public const string Email = "email";

    // The demo sign-in buttons.
    public const string DemoLogin = "demo-login";
}
