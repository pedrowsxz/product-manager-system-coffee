using System.ComponentModel.DataAnnotations;
using Moinho.Web.Models;

namespace Moinho.Web.ViewModels;

public class StaffListQuery
{
    public const int PageSize = 20;

    public string? Q { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; } // "active", "inactive", "locked" or "unconfirmed"
    public int Page { get; set; } = 1;
}

public enum StaffStatus
{
    Active,
    Unconfirmed,
    Locked,   // too many wrong passwords: temporary
    Inactive, // deactivated by an admin: LockoutEnd = DateTimeOffset.MaxValue
}

public record StaffRow(
    string Id,
    string FullName,
    string Email,
    string Role,
    StaffStatus Status,
    bool HasPassword,
    bool IsProtectedDemo,
    DateTime CreatedAt);

public class StaffCreateViewModel
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Nome completo")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Informe o email.")]
    [EmailAddress(ErrorMessage = "Email inválido.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Escolha o perfil.")]
    [Display(Name = "Perfil")]
    public string Role { get; set; } = Roles.Staff;
}

public class StaffEditViewModel
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Nome completo")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Informe o email.")]
    [EmailAddress(ErrorMessage = "Email inválido.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Escolha o perfil.")]
    [Display(Name = "Perfil")]
    public string Role { get; set; } = Roles.Staff;

    [Display(Name = "Ativo")]
    public bool IsActive { get; set; } = true;
}

public record DashboardViewModel(
    int ActiveProducts,
    int InactiveProducts,
    int ActiveStaff,
    int UnconfirmedStaff,
    int LockedStaff,
    int InactiveStaff,
    int ErrorsLast24h);
