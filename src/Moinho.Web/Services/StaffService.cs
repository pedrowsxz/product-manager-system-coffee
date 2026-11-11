using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Services;

public record StaffResult(bool Succeeded, string? Error = null, string? Field = null, bool EmailFailed = false)
{
    public static readonly StaffResult Ok = new(true);
    public static readonly StaffResult NotFound = new(false, "Usuário não encontrado.");

    public static StaffResult Fail(string error, string? field = null) => new(false, error, field);
}

// Staff management for the Admin area. Every write goes through UserManager (which validates and
// keeps Identity's own columns consistent); the database is only queried directly for the list.
public class StaffService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AccountEmails emails,
    IOptions<SeedOptions> seedOptions,
    TimeProvider time)
{
    // An admin deactivating someone locks them out "forever", until reactivated.
    public static readonly DateTimeOffset InactiveLockoutEnd = DateTimeOffset.MaxValue;

    public async Task<PagedList<StaffRow>> ListAsync(StaffListQuery query)
    {
        var now = time.GetUtcNow();
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email!.Contains(term));
        }

        if (Roles.All.Contains(query.Role))
        {
            var roleId = db.Roles.Where(r => r.Name == query.Role).Select(r => r.Id);
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && roleId.Contains(ur.RoleId)));
        }

        users = query.Status switch
        {
            "inactive" => users.Where(u => u.LockoutEnd == InactiveLockoutEnd),
            "locked" => users.Where(u => u.LockoutEnd > now && u.LockoutEnd != InactiveLockoutEnd),
            "unconfirmed" => users.Where(u => !u.EmailConfirmed),
            "active" => users.Where(u => u.EmailConfirmed && (u.LockoutEnd == null || u.LockoutEnd <= now)),
            _ => users,
        };

        var total = await users.CountAsync();
        var page = Math.Clamp(query.Page, 1, 10_000); // a huge page number would overflow Skip()
        var pageOfUsers = await users
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((page - 1) * StaffListQuery.PageSize)
            .Take(StaffListQuery.PageSize)
            .Select(u => new
            {
                User = u,
                Role = db.UserRoles.Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var rows = pageOfUsers.Select(x => ToRow(x.User, x.Role ?? "", now)).ToList();
        return new PagedList<StaffRow>(rows, page, StaffListQuery.PageSize, total);
    }

    public async Task<StaffRow?> GetAsync(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        return user is null ? null : ToRow(user, await RoleOfAsync(user), time.GetUtcNow());
    }

    public bool IsProtected(StaffRow row) => row.IsProtectedDemo || IsSeedAdmin(row.Email);

    // Admin adds a staff member: the account gets no password. The admin vouches for the address
    // (EmailConfirmed = true) and the person receives an invitation link to choose their own password.
    public async Task<StaffResult> InviteAsync(StaffCreateViewModel form)
    {
        if (!Roles.All.Contains(form.Role))
        {
            return StaffResult.Fail("Perfil inválido.", nameof(form.Role));
        }
        if (await userManager.FindByEmailAsync(form.Email) is not null)
        {
            return StaffResult.Fail("Já existe uma conta com este email.", nameof(form.Email));
        }

        var user = new ApplicationUser
        {
            UserName = form.Email,
            Email = form.Email,
            EmailConfirmed = true,
            FullName = form.FullName.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        var created = await userManager.CreateAsync(user); // no password on purpose
        if (!created.Succeeded)
        {
            return StaffResult.Fail(string.Join(" ", created.Errors.Select(e => e.Description)), nameof(form.Email));
        }
        Check(await userManager.AddToRoleAsync(user, form.Role));

        var sent = await emails.SendInvitationAsync(user);
        return new StaffResult(true, EmailFailed: !sent);
    }

    public async Task<StaffResult> UpdateAsync(string actingAdminId, string id, StaffEditViewModel form)
    {
        if (!Roles.All.Contains(form.Role))
        {
            return StaffResult.Fail("Perfil inválido.", nameof(form.Role));
        }

        // Serializable: the "is there another active admin?" check and the change happen as one unit,
        // so two admins demoting each other at the same moment can't leave the store with none.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return StaffResult.NotFound;
        }

        var currentRole = await RoleOfAsync(user);
        var isSelf = user.Id == actingAdminId;
        var demoting = currentRole == Roles.Admin && form.Role != Roles.Admin;
        var roleChanged = currentRole != form.Role;
        var deactivating = !form.IsActive && user.LockoutEnd != InactiveLockoutEnd;
        var emailChanged = !string.Equals(form.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase);

        // Rule 1: an admin can't remove their own access.
        if (isSelf && demoting)
        {
            return StaffResult.Fail("Você não pode remover seu próprio perfil de administrador.", nameof(form.Role));
        }
        if (isSelf && deactivating)
        {
            return StaffResult.Fail("Você não pode desativar a própria conta.", nameof(form.IsActive));
        }
        // Changing an email here signs the person out until they confirm it; for your own account
        // use Minha conta → Email, which only switches after the new address is confirmed.
        if (isSelf && emailChanged)
        {
            return StaffResult.Fail("Para trocar o seu próprio email, use Minha conta → Email.", nameof(form.Email));
        }

        // Rule 2: protected accounts (demo accounts and the seeded admin) keep their role, email and
        // access. Email matters too: whoever controls the address can reset the password.
        if (IsProtected(user) && (roleChanged || deactivating || emailChanged))
        {
            return StaffResult.Fail("Esta conta é protegida: perfil, email e situação não podem ser alterados.");
        }

        // Rule 3: never leave the store without an active admin. An email change also counts:
        // the admin can't sign in again until the new address is confirmed.
        if ((demoting || deactivating || emailChanged) && currentRole == Roles.Admin && IsActive(user)
            && await CountOtherActiveAdminsAsync(user.Id) == 0)
        {
            return StaffResult.Fail("Este é o último administrador ativo. Promova outra pessoa antes.");
        }

        user.FullName = form.FullName.Trim();
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return StaffResult.Fail(string.Join(" ", updated.Errors.Select(e => e.Description)));
        }

        if (emailChanged)
        {
            var email = form.Email.Trim();
            var setEmail = await userManager.SetEmailAsync(user, email); // also sets EmailConfirmed = false
            if (!setEmail.Succeeded)
            {
                return StaffResult.Fail(string.Join(" ", setEmail.Errors.Select(e => e.Description)), nameof(form.Email));
            }
            Check(await userManager.SetUserNameAsync(user, email));
        }

        if (roleChanged)
        {
            if (currentRole != "")
            {
                Check(await userManager.RemoveFromRoleAsync(user, currentRole));
            }
            Check(await userManager.AddToRoleAsync(user, form.Role));
        }

        Check(await userManager.SetLockoutEndDateAsync(user, form.IsActive ? null : InactiveLockoutEnd));

        // The cookie holds the old role; a new stamp makes the security-stamp validator sign the
        // user out on their next request (ValidationInterval = 0), so the change applies at once.
        Check(await userManager.UpdateSecurityStampAsync(user));

        await transaction.CommitAsync();

        // Sent after the commit: the link must point at the saved address.
        var emailFailed = emailChanged && !await emails.SendConfirmationAsync(user);
        return new StaffResult(true, EmailFailed: emailFailed);
    }

    public async Task<StaffResult> DeleteAsync(string actingAdminId, string id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return StaffResult.NotFound;
        }

        if (user.Id == actingAdminId)
        {
            return StaffResult.Fail("Você não pode excluir a própria conta.");
        }
        if (IsProtected(user))
        {
            return StaffResult.Fail("Esta conta é protegida e não pode ser excluída.");
        }
        if (await RoleOfAsync(user) == Roles.Admin && IsActive(user) && await CountOtherActiveAdminsAsync(user.Id) == 0)
        {
            return StaffResult.Fail("Este é o último administrador ativo e não pode ser excluído.");
        }

        // Hard delete. Products don't reference users, so nothing else needs to change.
        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return StaffResult.Fail(string.Join(" ", deleted.Errors.Select(e => e.Description)));
        }

        await transaction.CommitAsync();
        return StaffResult.Ok;
    }

    // Active admin = in the Admin role, email confirmed and not locked out (can actually sign in).
    private async Task<int> CountOtherActiveAdminsAsync(string excludeUserId)
    {
        var now = time.GetUtcNow();
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(u => u.Id != excludeUserId && u.EmailConfirmed && (u.LockoutEnd is null || u.LockoutEnd <= now));
    }

    private bool IsActive(ApplicationUser user) => user.EmailConfirmed && (user.LockoutEnd is null || user.LockoutEnd <= time.GetUtcNow());

    private bool IsProtected(ApplicationUser user) => user.IsProtectedDemo || IsSeedAdmin(user.Email);

    // The real admin from configuration is protected too, so a visitor using the demo Admin account
    // can't remove the owner's access (the demo is public).
    private bool IsSeedAdmin(string? email) =>
        string.Equals(email, seedOptions.Value.AdminEmail, StringComparison.OrdinalIgnoreCase);

    // For calls that can only fail because of a bug: throwing rolls the transaction back and the
    // error page logs it, instead of silently leaving a half-applied change.
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task<string> RoleOfAsync(ApplicationUser user) => (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "";

    private static StaffRow ToRow(ApplicationUser user, string role, DateTimeOffset now)
    {
        var status = user.LockoutEnd == InactiveLockoutEnd ? StaffStatus.Inactive
            : user.LockoutEnd > now ? StaffStatus.Locked
            : !user.EmailConfirmed ? StaffStatus.Unconfirmed
            : StaffStatus.Active;
        return new StaffRow(user.Id, user.FullName, user.Email ?? "", role, status, user.PasswordHash is not null, user.IsProtectedDemo, user.CreatedAt);
    }
}
