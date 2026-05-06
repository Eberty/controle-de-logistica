using AssetManagement.Data;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers;

public abstract class AuthenticatedControllerBase : ControllerBase
{
    protected readonly AppDbContext Context;
    protected readonly IAuthSessionStore SessionStore;

    protected AuthenticatedControllerBase(AppDbContext context, IAuthSessionStore sessionStore)
    {
        Context = context;
        SessionStore = sessionStore;
    }

    protected bool TryGetCurrentUser(out User user, out IActionResult? error)
    {
        user = default!;
        error = null;

        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = Unauthorized(new { message = "Missing bearer token" });
            return false;
        }

        var token = header[prefix.Length..].Trim();

        if (string.IsNullOrWhiteSpace(token) || !SessionStore.TryGetUser(token, out var storedUser) || storedUser is null)
        {
            error = Unauthorized(new { message = "Invalid session" });
            return false;
        }

        user = storedUser;
        return true;
    }

    protected bool TryGetAdminUser(out User user, out IActionResult? error)
    {
        if (!TryGetCurrentUser(out user, out error))
            return false;

        if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            error = Forbid();
            return false;
        }

        return true;
    }

}
