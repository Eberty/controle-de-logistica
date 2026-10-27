using System.Globalization;
using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/me/notes")]
public class NotesController : AuthenticatedControllerBase
{
    private const int MaxStoredTagsLength = 500;

    private readonly IAuditLogger _auditLogger;

    public NotesController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var notes = await Context.UserNotes
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.Id)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync();

        return Ok(await MapNotesWithRecipientsAsync(notes));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var (fields, validationError) = await ValidateNoteFieldsAsync(request, currentUser, string.Empty);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var now = DateTime.UtcNow;
        var note = new UserNote
        {
            UserId = currentUser.Id,
            Title = fields!.Title,
            Content = fields.Content,
            Tags = fields.Tags,
            SeiNumber = fields.SeiNumber,
            SeiProcessId = fields.SeiProcessId,
            DueDate = fields.DueDate,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.UserNotes.Add(note);
        await Context.SaveChangesAsync();
        Context.UserNoteRecipients.AddRange(fields.Recipients.Select(recipient => new UserNoteRecipient
        {
            NoteId = note.Id,
            UserId = recipient.Id,
            CreatedAt = now
        }));
        await Context.SaveChangesAsync();
        if (fields.Recipients.Count > 0)
            await LogMuralAsync(currentUser, AuditActions.Send, note, $"Anotação enviada para: {RecipientNames(fields.Recipients)}.");
        await transaction.CommitAsync();

        var created = (await MapNotesWithRecipientsAsync([note])).Single();
        return Created($"/api/me/notes/{note.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.Id);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        var (fields, validationError) = await ValidateNoteFieldsAsync(request, currentUser, note.Tags);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var currentRecipients = await Context.UserNoteRecipients
            .Where(x => x.NoteId == note.Id)
            .ToListAsync();
        var requestedIds = fields!.Recipients.Select(x => x.Id).ToHashSet();
        var removedRecipients = currentRecipients.Where(x => !requestedIds.Contains(x.UserId)).ToList();
        var addedRecipients = fields.Recipients
            .Where(x => currentRecipients.All(current => current.UserId != x.Id))
            .ToList();
        var contentChanged =
            !string.Equals(note.Title, fields.Title, StringComparison.Ordinal)
            || !string.Equals(note.Content, fields.Content, StringComparison.Ordinal)
            || !string.Equals(note.Tags, fields.Tags, StringComparison.Ordinal)
            || !string.Equals(note.SeiNumber, fields.SeiNumber, StringComparison.Ordinal)
            || !string.Equals(note.SeiProcessId, fields.SeiProcessId, StringComparison.Ordinal)
            || !string.Equals(note.DueDate, fields.DueDate, StringComparison.Ordinal);

        var now = DateTime.UtcNow;
        note.Title = fields.Title;
        note.Content = fields.Content;
        note.Tags = fields.Tags;
        note.SeiNumber = fields.SeiNumber;
        note.SeiProcessId = fields.SeiProcessId;
        note.DueDate = fields.DueDate;
        note.UpdatedAt = now;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.UserNoteRecipients.RemoveRange(removedRecipients);
        Context.UserNoteRecipients.AddRange(addedRecipients.Select(recipient => new UserNoteRecipient
        {
            NoteId = note.Id,
            UserId = recipient.Id,
            CreatedAt = now
        }));
        await Context.SaveChangesAsync();

        var details = new List<string>();
        if (addedRecipients.Count > 0)
            details.Add($"Enviada para: {RecipientNames(addedRecipients)}");
        if (removedRecipients.Count > 0)
        {
            var removedIds = removedRecipients.Select(x => x.UserId).ToList();
            var removedNames = await Context.Users
                .AsNoTracking()
                .Where(x => removedIds.Contains(x.Id))
                .Select(x => new { x.FullName, x.Username })
                .ToListAsync();
            details.Add($"Removida de: {string.Join(", ", removedNames.Select(x => DisplayNameFrom(x.FullName, x.Username)))}");
        }
        if (contentChanged && currentRecipients.Count > removedRecipients.Count)
            details.Add("Conteúdo editado");
        if (details.Count > 0)
            await LogMuralAsync(currentUser, AuditActions.Update, note, $"{string.Join(" | ", details)}.");
        await transaction.CommitAsync();

        return Ok((await MapNotesWithRecipientsAsync([note])).Single());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.Id);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        await using var transaction = await Context.Database.BeginTransactionAsync();
        var removedRecipients = await Context.UserNoteRecipients
            .Where(x => x.NoteId == note.Id)
            .ExecuteDeleteAsync();
        Context.UserNotes.Remove(note);
        await Context.SaveChangesAsync();
        if (removedRecipients > 0)
            await LogMuralAsync(currentUser, AuditActions.Remove, note, "Anotação excluída e removida do mural dos destinatários.");
        await transaction.CommitAsync();

        return NoContent();
    }

    private sealed record NoteFields(
        string Title,
        string Content,
        string Tags,
        string SeiNumber,
        string SeiProcessId,
        string DueDate,
        IReadOnlyList<User> Recipients);

    private Task LogMuralAsync(User actor, string action, UserNote note, string details) =>
        _auditLogger.LogAsync(actor, action, AuditEntityTypes.Mural, note.Id.ToString(), note.Title, details);

    private static string RecipientNames(IEnumerable<User> recipients) =>
        string.Join(", ", recipients.Select(UserDisplayName));

    private async Task<(NoteFields? Fields, string? Error)> ValidateNoteFieldsAsync(
        UserNoteRequest request,
        User currentUser,
        string currentTags)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            return (null, "Informe título e conteúdo da anotação.");

        var (tags, tagsError) = await ResolveTagsAsync(request.Tags, currentTags);
        if (tagsError is not null)
            return (null, tagsError);

        var recipientIds = (request.RecipientUserIds ?? [])
            .Where(x => x != currentUser.Id)
            .Distinct()
            .ToList();
        var recipients = await Context.Users
            .AsNoTracking()
            .Where(x => recipientIds.Contains(x.Id))
            .ToListAsync();
        if (recipients.Count != recipientIds.Count)
            return (null, "Destinatário inválido. Selecione usuários cadastrados no sistema.");

        var seiNumber = (request.SeiNumber ?? string.Empty).Trim();
        if (!SeiLinks.TryParseProcessId(request.SeiLink, out var seiProcessId))
            return (null, SeiLinks.InvalidLinkMessage);

        var dueDate = (request.DueDate ?? string.Empty).Trim();
        if (dueDate.Length > 0
            && !DateOnly.TryParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return (null, "Informe uma data válida para o prazo.");

        return (new NoteFields(title, content, tags, seiNumber, seiProcessId, dueDate, recipients), null);
    }

    private async Task<(string Tags, string? Error)> ResolveTagsAsync(string[]? requestedTags, string currentTags)
    {
        var names = (requestedTags ?? [])
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return (string.Empty, null);

        // Free-text tags saved before the standardized list existed stay valid on their own note.
        var available = await Context.NoteTags.AsNoTracking().Select(x => x.Name).ToListAsync();
        available.AddRange(currentTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var resolved = new List<string>();
        foreach (var name in names)
        {
            var match = available.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return (string.Empty, $"Etiqueta \"{name}\" não cadastrada. Escolha uma etiqueta da lista.");
            resolved.Add(match);
        }

        var tags = string.Join(", ", resolved);
        if (tags.Length > MaxStoredTagsLength)
            return (string.Empty, "Muitas etiquetas selecionadas para a anotação.");

        return (tags, null);
    }
}
