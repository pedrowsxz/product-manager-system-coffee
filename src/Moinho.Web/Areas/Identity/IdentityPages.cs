namespace Moinho.Web.Areas.Identity;

public static class IdentityPages
{
    // The Identity pages this app uses (all scaffolded into Areas/Identity/Pages).
    // Any other page from the Identity UI library gets its route removed in Program.cs, so it returns 404.
    public static readonly HashSet<string> Used =
    [
        "/Account/Login",
        "/Account/Logout",
        "/Account/Register",
        "/Account/RegisterConfirmation",
        "/Account/ConfirmEmail",
        "/Account/ConfirmEmailChange",
        "/Account/ResendEmailConfirmation",
        "/Account/ForgotPassword",
        "/Account/ForgotPasswordConfirmation",
        "/Account/ResetPassword",
        "/Account/ResetPasswordConfirmation",
        "/Account/Lockout",
        "/Account/Manage/Index",
        "/Account/Manage/ChangePassword",
        "/Account/Manage/Email",
    ];
}
