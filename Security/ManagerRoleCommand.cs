using LogiTrack.Models;
using Microsoft.AspNetCore.Identity;

namespace LogiTrack.Security;

public static class ManagerRoleCommand
{
    // Local operator command only: no HTTP endpoint can grant privileges.
    public static async Task RunAsync(IServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Register this user before assigning the Manager role.");
        if (!await roles.RoleExistsAsync("Manager"))
            EnsureSuccess(await roles.CreateAsync(new IdentityRole("Manager")));
        if (!await users.IsInRoleAsync(user, "Manager"))
            EnsureSuccess(await users.AddToRoleAsync(user, "Manager"));
        Console.WriteLine("Manager role assigned. Log in again to obtain a token with the role.");
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
