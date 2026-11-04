using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/mural")]
public class MuralController : AuthenticatedControllerBase
{
    private const int MaxNotes = 1000;

    private readonly IAuditLogger _auditLogger;

    public MuralController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var userId = currentUser.Id;
        var sentNoteIds = await QueryDeliveries()
            .Where(x => x.Recipient.ConcludedAt == null
                && x.Recipient.ForwardedAt == null
                && (x.Recipient.ForwardedByUserId == userId
                    || (x.Recipient.ForwardedByUserId == null && x.Note.UserId == userId)))
            .Select(x => x.Note.Id)
            .Distinct()
            .ToListAsync();
        var sentNotes = await LoadNotesAsync(sentNoteIds, x => x.CreatedAt);

        var receivedRows = await QueryDeliveries()
            .Where(x => x.Recipient.UserId == userId && x.Recipient.ConcludedAt == null && x.Recipient.ForwardedAt == null)
            .OrderByDescending(x => x.Note.CreatedAt)
            .Take(MaxNotes)
            .ToListAsync();
        var names = await LoadUserNamesAsync(receivedRows
            .Select(x => x.Note.UserId)
            .Concat(receivedRows.Where(x => x.Recipient.ForwardedByUserId is not null).Select(x => x.Recipient.ForwardedByUserId!.Value)));
        var received = receivedRows.Select(x => new ReceivedNoteDto(
            x.Note.Id,
            x.Note.Title,
            x.Note.Content,
            x.Note.Tags,
            x.Note.SeiNumber,
            x.Note.SeiProcessId,
            x.Note.DueDate,
            x.Note.UserId,
            names.GetValueOrDefault(x.Note.UserId, string.Empty),
            x.Note.CreatedAt,
            x.Note.UpdatedAt,
            x.Recipient.ReadAt,
            x.Recipient.CompletedAt,
            x.Recipient.ForwardedByUserId,
            x.Recipient.ForwardedByUserId is null ? string.Empty : names.GetValueOrDefault(x.Recipient.ForwardedByUserId.Value, string.Empty)))
            .ToList();

        // Administrators see every concluded delivery; other users see the ones they sent or received.
        var isAdmin = IsAdmin(currentUser);
        var concludedNoteIds = await QueryDeliveries()
            .Where(x => x.Recipient.ConcludedAt != null
                && x.Recipient.ForwardedAt == null
                && (isAdmin
                    || x.Recipient.UserId == userId
                    || x.Recipient.ForwardedByUserId == userId
                    || (x.Recipient.ForwardedByUserId == null && x.Note.UserId == userId)))
            .Select(x => x.Note.Id)
            .Distinct()
            .ToListAsync();
        var concludedNotes = await LoadNotesAsync(concludedNoteIds, x => x.UpdatedAt);

        return Ok(new MuralResponse(
            await MapNotesWithRecipientsAsync(sentNotes, (recipient, note) =>
                IsOpenDelivery(recipient) && DeliverySenderId(recipient, note) == userId),
            received,
            await MapNotesWithRecipientsAsync(concludedNotes, (recipient, note) =>
                recipient.ConcludedAt is not null
                && recipient.ForwardedAt is null
                && (isAdmin || recipient.UserId == userId || DeliverySenderId(recipient, note) == userId))));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var userId = currentUser.Id;
        var unreadReceived = await Context.UserNoteRecipients
            .CountAsync(x => x.UserId == userId && x.ReadAt == null && x.ConcludedAt == null && x.ForwardedAt == null);
        var unseenCompletions = await QueryDeliveries()
            .CountAsync(x => x.Recipient.CompletedAt != null
                && x.Recipient.CompletionSeenAt == null
                && x.Recipient.ConcludedAt == null
                && x.Recipient.ForwardedAt == null
                && (x.Recipient.ForwardedByUserId == userId
                    || (x.Recipient.ForwardedByUserId == null && x.Note.UserId == userId)));

        return Ok(new MuralSummaryDto(unreadReceived, unseenCompletions));
    }

    [HttpGet("recipients")]
    public async Task<IActionResult> Recipients()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var users = await Context.Users
            .AsNoTracking()
            .Where(x => x.Id != currentUser.Id)
            .Select(x => new { x.Id, x.FullName, x.Username })
            .ToListAsync();

        return Ok(users
            .Select(x => new NoteRecipientOptionDto(x.Id, DisplayNameFrom(x.FullName, x.Username)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    [HttpPost("{noteId:int}/read")]
    public async Task<IActionResult> MarkAsRead(int noteId)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var recipient = await Context.UserNoteRecipients
            .FirstOrDefaultAsync(x => x.NoteId == noteId && x.UserId == currentUser.Id);
        if (recipient is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (recipient.ReadAt is null)
        {
            recipient.ReadAt = DateTime.UtcNow;
            await Context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("{noteId:int}/done")]
    public async Task<IActionResult> MarkAsDone(int noteId)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var recipient = await Context.UserNoteRecipients
            .FirstOrDefaultAsync(x => x.NoteId == noteId && x.UserId == currentUser.Id);
        if (recipient is null)
            return NotFound(new { message = "Anotação não encontrada." });

        var deliveryError = OpenDeliveryError(recipient);
        if (deliveryError is not null)
            return Conflict(new { message = deliveryError });

        if (recipient.CompletedAt is not null)
            return Conflict(new { message = "Essa anotação já foi marcada como pronta." });

        var note = await Context.UserNotes.AsNoTracking().FirstAsync(x => x.Id == noteId);
        var senderName = await UserNameAsync(DeliverySenderId(recipient, note));
        var now = DateTime.UtcNow;
        recipient.ReadAt ??= now;
        recipient.CompletedAt = now;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.MarkDone,
            AuditEntityTypes.Mural,
            note.Id.ToString(),
            note.Title,
            $"Anotação marcada como pronta e devolvida a {senderName}.");
        await transaction.CommitAsync();

        return NoContent();
    }

    // Forwarding hands the note off: it leaves the forwarder's Recebidos and the original sender's Enviados, and the
    // new deliveries show up in the forwarder's Enviados.
    [HttpPost("{noteId:int}/forward")]
    public async Task<IActionResult> Forward(int noteId, [FromBody] ForwardNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var recipient = await Context.UserNoteRecipients
            .FirstOrDefaultAsync(x => x.NoteId == noteId && x.UserId == currentUser.Id);
        var note = recipient is null ? null : await Context.UserNotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == noteId);
        if (recipient is null || note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        var deliveryError = OpenDeliveryError(recipient);
        if (deliveryError is not null)
            return Conflict(new { message = deliveryError });

        if (recipient.CompletedAt is not null)
            return Conflict(new { message = "Essa anotação já foi marcada como pronta e não pode ser encaminhada." });

        var requestedIds = (request.RecipientUserIds ?? []).Distinct().ToList();
        var users = await Context.Users
            .AsNoTracking()
            .Where(x => requestedIds.Contains(x.Id))
            .ToListAsync();
        if (users.Count != requestedIds.Count)
            return BadRequest(new { message = "Destinatário inválido. Selecione usuários cadastrados no sistema." });

        var currentRecipientIds = await Context.UserNoteRecipients
            .Where(x => x.NoteId == noteId)
            .Select(x => x.UserId)
            .ToListAsync();
        var newRecipients = users
            .Where(x => x.Id != note.UserId && !currentRecipientIds.Contains(x.Id))
            .OrderBy(x => UserDisplayName(x), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (newRecipients.Count == 0)
            return BadRequest(new { message = "Selecione pelo menos um usuário que ainda não recebeu essa anotação." });

        var now = DateTime.UtcNow;
        recipient.ReadAt ??= now;
        recipient.ForwardedAt = now;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.UserNoteRecipients.AddRange(newRecipients.Select(user => new UserNoteRecipient
        {
            NoteId = noteId,
            UserId = user.Id,
            ForwardedByUserId = currentUser.Id,
            CreatedAt = now
        }));
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Forward,
            AuditEntityTypes.Mural,
            note.Id.ToString(),
            note.Title,
            $"Anotação encaminhada para: {string.Join(", ", newRecipients.Select(UserDisplayName))}.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("{noteId:int}/recipients/{userId:int}/conclude")]
    public async Task<IActionResult> Conclude(int noteId, int userId)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var (note, recipient, lookupError) = await FindDeliveryForSenderAsync(noteId, userId, currentUser);
        if (lookupError is not null) return lookupError;

        var deliveryError = OpenDeliveryError(recipient!);
        if (deliveryError is not null)
            return Conflict(new { message = deliveryError });

        if (recipient!.CompletedAt is null)
            return Conflict(new { message = "A anotação só pode ser concluída depois que o destinatário marcar Pronto." });

        recipient.ConcludedAt = DateTime.UtcNow;
        recipient.ConcludedByUserId = currentUser.Id;
        var recipientName = await UserNameAsync(recipient.UserId);

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await SyncNoteCalendarEntryAsync(note!);
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Conclude,
            AuditEntityTypes.Mural,
            note!.Id.ToString(),
            note.Title,
            $"Anotação de {recipientName} concluída e movida para a aba Concluídos.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("{noteId:int}/recipients/{userId:int}/reopen")]
    public async Task<IActionResult> Reopen(int noteId, int userId)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var (note, recipient, lookupError) = await FindDeliveryForSenderAsync(noteId, userId, currentUser);
        if (lookupError is not null) return lookupError;

        if (recipient!.ConcludedAt is null || recipient.ForwardedAt is not null)
            return Conflict(new { message = "Essa anotação não está concluída." });

        recipient.ConcludedAt = null;
        recipient.ConcludedByUserId = null;
        var recipientName = await UserNameAsync(recipient.UserId);

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await SyncNoteCalendarEntryAsync(note!);
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Reopen,
            AuditEntityTypes.Mural,
            note!.Id.ToString(),
            note.Title,
            $"Anotação de {recipientName} reaberta e devolvida a Enviados e Recebidos.");
        await transaction.CommitAsync();

        return NoContent();
    }

    // Removes concluded deliveries; a note left with no open or concluded delivery is deleted with its calendar entry.
    [HttpPost("concluded/delete")]
    public async Task<IActionResult> DeleteConcluded([FromBody] DeleteConcludedNotesRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var keys = (request.Items ?? []).Distinct().ToList();
        if (keys.Count == 0)
            return BadRequest(new { message = "Selecione as anotações concluídas que deseja excluir." });

        var noteIds = keys.Select(x => x.NoteId).Distinct().ToList();
        var notes = await Context.UserNotes.Where(x => noteIds.Contains(x.Id)).ToListAsync();
        var recipients = await Context.UserNoteRecipients.Where(x => noteIds.Contains(x.NoteId)).ToListAsync();
        var selected = keys
            .Select(key => recipients.FirstOrDefault(x => x.NoteId == key.NoteId && x.UserId == key.UserId))
            .ToList();
        if (selected.Any(x => x is null || x.ConcludedAt is null || x.ForwardedAt is not null))
            return BadRequest(new { message = "Só é possível excluir anotações concluídas." });

        var names = await LoadUserNamesAsync(selected.Select(x => x!.UserId));

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.UserNoteRecipients.RemoveRange(selected!);
        var deletedNotes = new List<UserNote>();
        foreach (var note in notes)
        {
            var remaining = recipients.Where(x => x.NoteId == note.Id && !selected.Contains(x)).ToList();
            if (remaining.Any(x => x.ForwardedAt is null))
                continue;

            Context.UserNoteRecipients.RemoveRange(remaining);
            Context.UserNotes.Remove(note);
            deletedNotes.Add(note);
        }
        await Context.SaveChangesAsync();

        var deletedNoteIds = deletedNotes.Select(x => (int?)x.Id).ToList();
        await Context.CalendarEntries
            .Where(x => deletedNoteIds.Contains(x.NoteId))
            .ExecuteDeleteAsync();
        foreach (var group in selected.GroupBy(x => x!.NoteId))
        {
            var note = notes.First(x => x.Id == group.Key);
            var who = string.Join(", ", group.Select(x => names.GetValueOrDefault(x!.UserId, string.Empty)));
            var details = deletedNotes.Contains(note)
                ? $"Anotação concluída de {who} excluída do sistema."
                : $"Anotação concluída de {who} excluída da aba Concluídos.";
            await _auditLogger.LogAsync(currentUser, AuditActions.Delete, AuditEntityTypes.Mural, note.Id.ToString(), note.Title, details);
        }
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("sent/seen")]
    public async Task<IActionResult> MarkCompletionsAsSeen()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var userId = currentUser.Id;
        var ownNoteIds = Context.UserNotes
            .Where(x => x.UserId == userId)
            .Select(x => x.Id);
        var now = DateTime.UtcNow;
        await Context.UserNoteRecipients
            .Where(x => x.CompletedAt != null
                && x.CompletionSeenAt == null
                && (x.ForwardedByUserId == userId || (x.ForwardedByUserId == null && ownNoteIds.Contains(x.NoteId))))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CompletionSeenAt, now));

        return NoContent();
    }

    private sealed class Delivery
    {
        public UserNoteRecipient Recipient { get; init; } = default!;
        public UserNote Note { get; init; } = default!;
    }

    private IQueryable<Delivery> QueryDeliveries() =>
        Context.UserNoteRecipients
            .AsNoTracking()
            .Join(
                Context.UserNotes.AsNoTracking(),
                recipient => recipient.NoteId,
                note => note.Id,
                (recipient, note) => new Delivery { Recipient = recipient, Note = note });

    private async Task<List<UserNote>> LoadNotesAsync(
        List<int> noteIds,
        System.Linq.Expressions.Expression<Func<UserNote, DateTime>> orderBy) =>
        await Context.UserNotes
            .AsNoTracking()
            .Where(x => noteIds.Contains(x.Id))
            .OrderByDescending(orderBy)
            .Take(MaxNotes)
            .ToListAsync();

    private async Task<(UserNote? Note, UserNoteRecipient? Recipient, IActionResult? Error)> FindDeliveryForSenderAsync(
        int noteId,
        int userId,
        User currentUser)
    {
        var recipient = await Context.UserNoteRecipients.FirstOrDefaultAsync(x => x.NoteId == noteId && x.UserId == userId);
        var note = recipient is null ? null : await Context.UserNotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == noteId);
        if (recipient is null || note is null)
            return (null, null, NotFound(new { message = "Anotação não encontrada." }));

        if (DeliverySenderId(recipient, note) != currentUser.Id && !IsAdmin(currentUser))
            return (null, null, StatusCode(403, new { message = "Apenas quem enviou a anotação pode concluí-la ou reabri-la." }));

        return (note, recipient, null);
    }

    private static string? OpenDeliveryError(UserNoteRecipient recipient)
    {
        if (recipient.ForwardedAt is not null)
            return "Essa anotação já foi encaminhada.";
        if (recipient.ConcludedAt is not null)
            return ConcludedNoteMessage;
        return null;
    }

    private async Task<string> UserNameAsync(int userId) =>
        (await LoadUserNamesAsync([userId])).GetValueOrDefault(userId, string.Empty);
}
