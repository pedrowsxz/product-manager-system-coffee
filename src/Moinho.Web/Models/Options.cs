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
