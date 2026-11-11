namespace Moinho.Web.Models;

public static class Policies
{
    public const string RequireAdmin = "RequireAdmin"; // role Admin
    public const string RequireStaff = "RequireStaff"; // role Admin or Staff
}
