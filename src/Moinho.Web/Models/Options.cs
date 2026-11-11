namespace Moinho.Web.Models;

public class SeedOptions
{
    public string AdminEmail { get; set; } = "";
    public string AdminPassword { get; set; } = "";
    public string AdminName { get; set; } = "Administrador";
}

public class DemoOptions
{
    public bool Enabled { get; set; }
    public bool EnableErrorSimulation { get; set; }
    public bool NightlyReset { get; set; }
}

public class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "";
    public bool UseStartTls { get; set; } = true;
}

public class RateLimitOptions
{
    // POSTs that send email (register, forgot password, resend confirmation, ...) per IP per window.
    public int EmailPermitLimit { get; set; } = 5;
    public int EmailWindowMinutes { get; set; } = 15;

    // Admin staff-edit POSTs per IP per window.
    public int AdminEditPermitLimit { get; set; } = 30;
    public int AdminEditWindowMinutes { get; set; } = 15;

    // Demo sign-in POSTs per IP per window.
    public int DemoLoginPermitLimit { get; set; } = 10;
    public int DemoLoginWindowMinutes { get; set; } = 10;
}
