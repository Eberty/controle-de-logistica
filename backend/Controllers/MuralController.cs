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
                && Context.UserNoteRecipients.Any(recipient => recipient.NoteId == x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaxNotes)
            .ToListAsync();

        var receivedRows = await Context.UserNoteRecipients
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.Id)
            .Join(
                Context.UserNotes.AsNoTracking(),
                recipient => recipient.NoteId,
                note => note.Id,
                (recipient, note) => new { recipient, note })
            .Join(
                Context.Users.AsNoTracking(),
                row => row.note.UserId,
                user => user.Id,
                (row, user) => new { row.recipient, row.note, user.FullName, user.Username })
            .OrderByDescending(x => x.note.CreatedAt)
            .Take(MaxNotes)
            .ToListAsync();

        var received = receivedRows.Select(x => new ReceivedNoteDto(
            x.note.Id,
            x.note.Title,
            x.note.Content,
            x.note.Tags,
            x.note.SeiNumber,
            x.note.SeiProcessId,
            x.note.DueDate,
            x.note.UserId,
            DisplayNameFrom(x.FullName, x.Username),
            x.note.CreatedAt,
            x.note.UpdatedAt,
            x.recipient.ReadAt,
            x.recipient.CompletedAt)).ToList();

        return Ok(new MuralResponse(await MapNotesWithRecipientsAsync(sentNotes), received));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var unreadReceived = await Context.UserNoteRecipients
            .CountAsync(x => x.UserId == currentUser.Id && x.ReadAt == null);
        var unseenCompletions = await Context.UserNoteRecipients
            .Where(x => x.CompletedAt != null && x.CompletionSeenAt == null)
            .Join(
                Context.UserNotes.Where(note => note.UserId == currentUser.Id),
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
        var now = DateTime.UtcNow;
        recipient.ReadAt ??= now;
        recipient.CompletedAt = now;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Complete,
            AuditEntityTypes.Mural,
            note.Id.ToString(),
            note.Title,
            "Anotação marcada como pronta e devolvida ao remetente.");
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
