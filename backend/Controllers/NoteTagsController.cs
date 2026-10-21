using System.Text.RegularExpressions;
using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/note-tags")]
public partial class NoteTagsController : AuthenticatedControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public NoteTagsController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var tags = await Context.NoteTags
            .AsNoTracking()
            .Select(x => new NoteTagDto(x.Id, x.Name, x.Color))
            .ToListAsync();

        return Ok(tags.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NoteTagRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Informe o nome da etiqueta." });

        // Tags are stored comma-separated on the note.
        if (name.Contains(','))
            return BadRequest(new { message = "O nome da etiqueta não pode conter vírgula." });

        var color = (request.Color ?? string.Empty).Trim().ToLowerInvariant();
        if (!HexColorRegex().IsMatch(color))
            return BadRequest(new { message = "Cor inválida para a etiqueta." });

        var existingNames = await Context.NoteTags.AsNoTracking().Select(x => x.Name).ToListAsync();
        if (existingNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "Essa etiqueta já existe." });

        var tag = new NoteTag
        {
            Name = name,
            Color = color,
            CreatedByUserId = currentUser.Id,
            CreatedAt = DateTime.UtcNow
        };

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.NoteTags.Add(tag);
        try
        {
            await Context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { message = "Essa etiqueta já existe." });
        }
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Create,
            AuditEntityTypes.NoteTag,
            tag.Id.ToString(),
            tag.Name,
            $"Etiqueta cadastrada com a cor {tag.Color}.");
        await transaction.CommitAsync();

        return Created($"/api/note-tags/{tag.Id}", new NoteTagDto(tag.Id, tag.Name, tag.Color));
    }

    // Notes keep the tag name as text, so they still show it after the tag leaves the list.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var tag = await Context.NoteTags.FirstOrDefaultAsync(x => x.Id == id);
        if (tag is null)
            return NotFound(new { message = "Etiqueta não encontrada." });

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.NoteTags.Remove(tag);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Delete,
            AuditEntityTypes.NoteTag,
            tag.Id.ToString(),
            tag.Name,
            "Etiqueta removida da lista. As anotações que já usavam a etiqueta mantêm o nome.");
        await transaction.CommitAsync();

        return NoContent();
    }

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex HexColorRegex();
}
