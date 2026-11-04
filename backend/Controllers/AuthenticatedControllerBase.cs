using AssetManagement.Data;
using AssetManagement.Dtos;
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

    protected const string ConcludedNoteMessage =
        "Anotação concluída não pode ser alterada. Ela pode ser reaberta na aba Concluídos do Mural.";

    // Each recipient row is a delivery: the sender is whoever forwarded it, or the note author. A delivery stays open
    // until the recipient forwards it (hand-off) or the sender concludes it.
    protected static bool IsOpenDelivery(UserNoteRecipient recipient) =>
        recipient.ConcludedAt is null && recipient.ForwardedAt is null;

    protected static int DeliverySenderId(UserNoteRecipient recipient, UserNote note) =>
        recipient.ForwardedByUserId ?? note.UserId;

    // A note is closed when it was sent and none of its deliveries is open anymore.
    protected static bool IsClosedNote(IReadOnlyCollection<UserNoteRecipient> recipients) =>
        recipients.Count > 0 && !recipients.Any(IsOpenDelivery);

    protected async Task<bool> IsClosedNoteAsync(int noteId) =>
        IsClosedNote(await Context.UserNoteRecipients.AsNoTracking().Where(x => x.NoteId == noteId).ToListAsync());

    protected async Task<List<UserNoteDto>> MapNotesWithRecipientsAsync(
        IReadOnlyCollection<UserNote> notes,
        Func<UserNoteRecipient, UserNote, bool>? includeRecipient = null)
    {
        var noteIds = notes.Select(x => x.Id).ToList();
        var recipients = await Context.UserNoteRecipients
            .AsNoTracking()
            .Where(x => noteIds.Contains(x.NoteId))
            .ToListAsync();
        var names = await LoadUserNamesAsync(notes
            .Select(x => x.UserId)
            .Concat(recipients.Select(x => x.UserId))
            .Concat(recipients.Where(x => x.ForwardedByUserId is not null).Select(x => x.ForwardedByUserId!.Value))
            .Concat(recipients.Where(x => x.ConcludedByUserId is not null).Select(x => x.ConcludedByUserId!.Value)));

        return notes.Select(note =>
        {
            var noteRecipients = recipients.Where(x => x.NoteId == note.Id).ToList();
            var lastConclusion = IsClosedNote(noteRecipients)
                ? noteRecipients.Where(x => x.ConcludedAt is not null).MaxBy(x => x.ConcludedAt)
                : null;
            return new UserNoteDto(
                note.Id,
                note.Title,
                note.Content,
                note.Tags,
                note.SeiNumber,
                note.SeiProcessId,
                note.DueDate,
                note.UserId,
                names.GetValueOrDefault(note.UserId, string.Empty),
                note.CreatedAt,
                note.UpdatedAt,
                noteRecipients
                    .Where(x => includeRecipient is null || includeRecipient(x, note))
                    .Select(x => ToRecipientDto(x, names))
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                lastConclusion?.ConcludedAt,
                lastConclusion?.ConcludedByUserId is int concludedBy ? names.GetValueOrDefault(concludedBy, string.Empty) : string.Empty);
        }).ToList();
    }

    protected static NoteRecipientDto ToRecipientDto(UserNoteRecipient recipient, IReadOnlyDictionary<int, string> names) => new(
        recipient.UserId,
        names.GetValueOrDefault(recipient.UserId, string.Empty),
        recipient.ReadAt,
        recipient.CompletedAt,
        recipient.CompletionSeenAt is not null,
        recipient.ForwardedByUserId,
        recipient.ForwardedByUserId is null ? string.Empty : names.GetValueOrDefault(recipient.ForwardedByUserId.Value, string.Empty),
        recipient.ConcludedAt,
        recipient.ConcludedByUserId is null ? string.Empty : names.GetValueOrDefault(recipient.ConcludedByUserId.Value, string.Empty));

    protected async Task<Dictionary<int, string>> LoadUserNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        return await Context.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => DisplayNameFrom(x.FullName, x.Username));
    }

    // Keeps the calendar entry of a note with a deadline in step with the note while it is private or has an open
    // delivery. CalendarController shows it only to the users of the open deliveries, or to the author of a private note.
    protected async Task SyncNoteCalendarEntryAsync(UserNote note)
    {
        var entry = await Context.CalendarEntries.FirstOrDefaultAsync(x => x.NoteId == note.Id);
        var recipients = await Context.UserNoteRecipients.AsNoTracking().Where(x => x.NoteId == note.Id).ToListAsync();
        if (IsClosedNote(recipients) || string.IsNullOrEmpty(note.DueDate))
        {
            if (entry is not null)
            {
                Context.CalendarEntries.Remove(entry);
                await Context.SaveChangesAsync();
            }
            return;
        }

        var now = DateTime.UtcNow;
        if (entry is null)
        {
            var author = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == note.UserId);
            entry = new CalendarEntry
            {
                NoteId = note.Id,
                CreatedByUserId = author.Id,
                CreatedByUserName = UserDisplayName(author),
                CreatedAt = now
            };
            Context.CalendarEntries.Add(entry);
        }

        entry.DueDate = note.DueDate;
        entry.SeiNumber = note.SeiNumber;
        entry.SeiProcessId = note.SeiProcessId;
        entry.Subject = note.Title;
        entry.Notes = recipients.Count > 0 ? "Prazo de anotação enviada pelo Mural." : "Prazo de anotação privada.";
        entry.UpdatedAt = now;
        await Context.SaveChangesAsync();
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
