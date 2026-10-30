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

        var sentNotes = await Context.UserNotes
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.Id
                && x.ConcludedAt == null
                && Context.UserNoteRecipients.Any(recipient => recipient.NoteId == x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaxNotes)
            .ToListAsync();

        var receivedRows = await Context.UserNoteRecipients
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.Id)
            .Join(
                Context.UserNotes.AsNoTracking().Where(note => note.ConcludedAt == null),
                recipient => recipient.NoteId,
                note => note.Id,
                (recipient, note) => new { recipient, note })
            .OrderByDescending(x => x.note.CreatedAt)
            .Take(MaxNotes)
            .ToListAsync();

        var receivedNoteIds = receivedRows.Select(x => x.note.Id).ToList();
        var forwards = await Context.UserNoteRecipients
            .AsNoTracking()
            .Where(x => receivedNoteIds.Contains(x.NoteId) && x.ForwardedByUserId == currentUser.Id)
            .ToListAsync();
        var names = await LoadUserNamesAsync(receivedRows
            .Select(x => x.note.UserId)
            .Concat(receivedRows.Where(x => x.recipient.ForwardedByUserId is not null).Select(x => x.recipient.ForwardedByUserId!.Value))
            .Concat(forwards.Select(x => x.UserId)));

        var received = receivedRows.Select(x => new ReceivedNoteDto(
            x.note.Id,
            x.note.Title,
            x.note.Content,
            x.note.Tags,
            x.note.SeiNumber,
            x.note.SeiProcessId,
            x.note.DueDate,
            x.note.UserId,
            names.GetValueOrDefault(x.note.UserId, string.Empty),
            x.note.CreatedAt,
            x.note.UpdatedAt,
            x.recipient.ReadAt,
            x.recipient.CompletedAt,
            x.recipient.ForwardedByUserId is null ? string.Empty : names.GetValueOrDefault(x.recipient.ForwardedByUserId.Value, string.Empty),
            forwards
                .Where(forward => forward.NoteId == x.note.Id)
                .Select(forward => ToRecipientDto(forward, names))
                .OrderBy(forward => forward.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList())).ToList();

        // Administrators see every concluded note; other users see the ones they sent or received.
        var concludedQuery = Context.UserNotes.AsNoTracking().Where(x => x.ConcludedAt != null);
        if (!IsAdmin(currentUser))
            concludedQuery = concludedQuery.Where(x => x.UserId == currentUser.Id
                || Context.UserNoteRecipients.Any(recipient => recipient.NoteId == x.Id && recipient.UserId == currentUser.Id));
        var concludedNotes = await concludedQuery
            .OrderByDescending(x => x.ConcludedAt)
            .Take(MaxNotes)
            .ToListAsync();

        return Ok(new MuralResponse(
            await MapNotesWithRecipientsAsync(sentNotes),
            received,
            await MapNotesWithRecipientsAsync(concludedNotes)));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var openNotes = Context.UserNotes.Where(note => note.ConcludedAt == null);
        var unreadReceived = await Context.UserNoteRecipients
            .Where(x => x.UserId == currentUser.Id && x.ReadAt == null)
            .Join(openNotes, recipient => recipient.NoteId, note => note.Id, (recipient, _) => recipient)
            .CountAsync();
        var unseenCompletions = await Context.UserNoteRecipients
            .Where(x => x.CompletedAt != null && x.CompletionSeenAt == null)
            .Join(
                openNotes.Where(note => note.UserId == currentUser.Id),
                recipient => recipient.NoteId,
                note => note.Id,
                (recipient, _) => recipient)
            .CountAsync();

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

        if (recipient.CompletedAt is not null)
            return Conflict(new { message = "Essa anotação já foi marcada como pronta." });

        var note = await Context.UserNotes.AsNoTracking().FirstAsync(x => x.Id == noteId);
        if (note.ConcludedAt is not null)
            return Conflict(new { message = ConcludedNoteMessage });
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
            "Anotação marcada como pronta e devolvida ao remetente.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("{noteId:int}/forward")]
    public async Task<IActionResult> Forward(int noteId, [FromBody] ForwardNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var isRecipient = await Context.UserNoteRecipients
            .AnyAsync(x => x.NoteId == noteId && x.UserId == currentUser.Id);
        var note = isRecipient ? await Context.UserNotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == noteId) : null;
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (note.ConcludedAt is not null)
            return Conflict(new { message = ConcludedNoteMessage });

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

    [HttpPost("{noteId:int}/conclude")]
    public async Task<IActionResult> Conclude(int noteId)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == noteId);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (note.ConcludedAt is not null)
            return Conflict(new { message = "Essa anotação já foi concluída." });

        var hasCompletion = await Context.UserNoteRecipients
            .AnyAsync(x => x.NoteId == noteId && x.CompletedAt != null);
        if (!hasCompletion)
            return Conflict(new { message = "A anotação só pode ser concluída depois que um destinatário marcar Pronto." });

        note.ConcludedAt = DateTime.UtcNow;
        note.ConcludedByUserId = currentUser.Id;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await SyncNoteCalendarEntryAsync(note);
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Conclude,
            AuditEntityTypes.Mural,
            note.Id.ToString(),
            note.Title,
            "Anotação concluída e movida para a aba Concluídos.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("{noteId:int}/reopen")]
    public async Task<IActionResult> Reopen(int noteId)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == noteId);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (note.ConcludedAt is null)
            return Conflict(new { message = "Essa anotação não está concluída." });

        note.ConcludedAt = null;
        note.ConcludedByUserId = null;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await SyncNoteCalendarEntryAsync(note);
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Reopen,
            AuditEntityTypes.Mural,
            note.Id.ToString(),
            note.Title,
            "Anotação reaberta e devolvida a Enviados e Recebidos.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPost("sent/seen")]
    public async Task<IActionResult> MarkCompletionsAsSeen()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var ownNoteIds = Context.UserNotes
            .Where(x => x.UserId == currentUser.Id)
            .Select(x => x.Id);
        var now = DateTime.UtcNow;
        await Context.UserNoteRecipients
            .Where(x => ownNoteIds.Contains(x.NoteId) && x.CompletedAt != null && x.CompletionSeenAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CompletionSeenAt, now));

        return NoContent();
    }
}
