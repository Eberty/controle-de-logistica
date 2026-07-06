using AssetManagement.Data;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    protected async Task<(bool Ok, User User, IActionResult? Error)> TryGetCurrentUserAsync()
    {
        if (!TryGetBearerToken(out var token))
            return (false, default!, Unauthorized(new { message = "Token de acesso ausente." }));

        var user = await SessionStore.TryGetUserAsync(token);
        if (user is null)
            return (false, default!, Unauthorized(new { message = "Sessão inválida." }));

        return (true, user, null);
    }

    protected async Task<(bool Ok, User User, IActionResult? Error)> TryGetAdminUserAsync()
    {
        var (ok, user, error) = await TryGetCurrentUserAsync();
        if (!ok) return (false, default!, error);

        if (!IsAdmin(user))
            return (false, default!, StatusCode(403, new { message = "Acesso restrito a administradores." }));

        return (true, user, null);
    }

    protected bool TryGetBearerToken(out string token)
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = header[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    protected const int MaxItemQuantity = 1_000_000;

    protected static string? ValidateRequestedQuantity(int quantity)
    {
        if (quantity < 1)
            return "A quantidade deve ser de pelo menos 1.";
        if (quantity > MaxItemQuantity)
            return "A quantidade deve ser de no máximo 1000000.";
        return null;
    }

    protected static bool ExceedsQuantityLimit(int currentQuantity, int addedQuantity) =>
        (long)currentQuantity + addedQuantity > MaxItemQuantity;

    protected static string FormatBoolean(bool value) => value ? "Sim" : "Não";

    protected static string UserDisplayName(User user) =>
        DisplayNameFrom(user.FullName, user.Username);

    protected static string DisplayNameFrom(string? fullName, string username) =>
        string.IsNullOrWhiteSpace(fullName) ? username : fullName;

    protected static bool IsAdmin(User user) =>
        string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    protected static string FormatAuditValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(vazio)" : value;

    protected static string NormalizeResponsiblePerson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    protected static void TrackPhotoFile(string? fileName, List<string> fileNames)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
            fileNames.Add(fileName);
    }

    protected Task AcquireItemsWriteLockAsync() =>
        Context.Database.ExecuteSqlRawAsync("""
            UPDATE "AppLocks"
            SET "TouchedAt" = "TouchedAt"
            WHERE "Name" = 'Items'
            """);

    protected Task TouchItemForWriteAsync(int itemId) =>
        Context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Items"
            SET "UpdatedAt" = "UpdatedAt"
            WHERE "Id" = {itemId}
            """);

    protected IQueryable<User> QueryUsersByUsername(string username)
    {
        if (Context.Database.IsSqlite())
            return Context.Users.Where(x => EF.Functions.Collate(x.Username, "NOCASE") == username);

        var normalizedUsername = username.ToLowerInvariant();
        return Context.Users.Where(x => x.Username.ToLower() == normalizedUsername);
    }

    protected IQueryable<Item> QueryItemsByLocation(string location)
    {
        if (Context.Database.IsSqlite())
            return Context.Items.Where(x => EF.Functions.Collate(x.Location, "NOCASE") == location);

        var normalizedLocation = location.ToLowerInvariant();
        return Context.Items.Where(x => x.Location.ToLower() == normalizedLocation);
    }

    protected async Task<string?> ResolveKnownLocationAsync(string location)
    {
        var defaultLocation = CatalogRules.ResolveDefaultLocation(location);
        if (defaultLocation is not null)
            return defaultLocation;

        var trimmed = location.Trim();
        var options = await Context.LocationOptions.AsNoTracking().ToListAsync();
        return options
            .FirstOrDefault(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?.Name;
    }

    protected async Task ReassignItemAuditLogsAsync(int fromItemId, int toItemId)
    {
        await Context.AuditLogs
            .Where(x => x.EntityType == AuditEntityTypes.Item && x.EntityId == fromItemId.ToString())
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EntityId, toItemId.ToString()));
    }

    protected sealed record ItemMatchCriteria(
        string Name,
        string AssetTag,
        string Nature,
        string Location,
        string Condition,
        string Notes,
        bool IsDischarged,
        string? ResponsiblePerson = null,
        int? IgnoredItemId = null);

    protected async Task<Item?> FindMatchingInventoryItemAsync(ItemMatchCriteria criteria)
    {
        var normalizedResponsiblePerson = NormalizeResponsiblePerson(criteria.ResponsiblePerson);
        var candidates = await Context.Items
            .Where(x => x.AssetTag == criteria.AssetTag
                && x.IsDischarged == criteria.IsDischarged
                && x.Notes == criteria.Notes)
            .ToListAsync();

        return candidates.FirstOrDefault(x =>
            x.Id != criteria.IgnoredItemId
            && string.Equals(x.Name, criteria.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Nature, criteria.Nature, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Location, criteria.Location, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Condition, criteria.Condition, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Notes, criteria.Notes, StringComparison.Ordinal)
            && string.Equals(
                NormalizeResponsiblePerson(x.ResponsiblePerson),
                normalizedResponsiblePerson,
                StringComparison.OrdinalIgnoreCase));
    }

    protected async Task MergeItemAsync(Item fromItem, int toItemId)
    {
        await Context.Movements
            .Where(x => x.ItemId == fromItem.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, toItemId));
        await Context.Movements
            .Where(x => x.DestinationItemId == fromItem.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DestinationItemId, (int?)toItemId));
        await ReassignItemAuditLogsAsync(fromItem.Id, toItemId);
        Context.Items.Remove(fromItem);
    }

}
