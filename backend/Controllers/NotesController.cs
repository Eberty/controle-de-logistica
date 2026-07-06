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

        return Ok(notes);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var (title, content, validationError) = ValidateNoteFields(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var now = DateTime.UtcNow;
        var note = new UserNote
        {
            UserId = currentUser.Id,
            Title = title,
            Content = content,
            Tags = NormalizeTags(request.Tags),
            IsPublic = request.IsPublic ?? false,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.UserNotes.Add(note);
        await Context.SaveChangesAsync();
        if (note.IsPublic)
            await LogMuralAsync(currentUser, AuditActions.Publish, note, "Anotação publicada no mural.");
        await transaction.CommitAsync();

        return Created($"/api/me/notes/{note.Id}", note);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserNoteRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.Id);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        var (title, content, validationError) = ValidateNoteFields(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var tags = NormalizeTags(request.Tags);
        var wasPublic = note.IsPublic;
        var contentChanged =
            !string.Equals(note.Title, title, StringComparison.Ordinal)
            || !string.Equals(note.Content, content, StringComparison.Ordinal)
            || !string.Equals(note.Tags, tags, StringComparison.Ordinal);

        note.Title = title;
        note.Content = content;
        note.Tags = tags;
        note.IsPublic = request.IsPublic ?? note.IsPublic;
        note.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        if (!wasPublic && note.IsPublic)
            await LogMuralAsync(currentUser, AuditActions.Publish, note, "Anotação publicada no mural.");
        else if (wasPublic && !note.IsPublic)
            await LogMuralAsync(currentUser, AuditActions.Remove, note, "Anotação removida do mural.");
        else if (wasPublic && note.IsPublic && contentChanged)
            await LogMuralAsync(currentUser, AuditActions.Update, note, "Anotação do mural editada.");
        await transaction.CommitAsync();

        return Ok(note);
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
        Context.UserNotes.Remove(note);
        await Context.SaveChangesAsync();
        if (note.IsPublic)
            await LogMuralAsync(currentUser, AuditActions.Remove, note, "Anotação excluída e removida do mural.");
        await transaction.CommitAsync();

        return NoContent();
    }

    private Task LogMuralAsync(User actor, string action, UserNote note, string details) =>
        _auditLogger.LogAsync(actor, action, AuditEntityTypes.Mural, note.Id.ToString(), note.Title, details);

    private static (string Title, string Content, string? Error) ValidateNoteFields(UserNoteRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            return (title, content, "Informe título e conteúdo da anotação.");
        return (title, content, null);
    }

    private static string NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return string.Empty;

        return string.Join(", ", tags
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }
}
