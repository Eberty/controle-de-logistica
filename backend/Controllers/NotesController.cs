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
    public NotesController(AppDbContext context, IAuthSessionStore sessionStore)
        : base(context, sessionStore)
    {
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

        var title = request.Title?.Trim() ?? string.Empty;
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            return BadRequest(new { message = "Informe título e conteúdo da anotação." });

        var now = DateTime.UtcNow;
        var note = new UserNote
        {
            UserId = currentUser.Id,
            Title = title,
            Content = content,
            Tags = NormalizeTags(request.Tags),
            CreatedAt = now,
            UpdatedAt = now
        };

        Context.UserNotes.Add(note);
        await Context.SaveChangesAsync();
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

        var title = request.Title?.Trim() ?? string.Empty;
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            return BadRequest(new { message = "Informe título e conteúdo da anotação." });

        note.Title = title;
        note.Content = content;
        note.Tags = NormalizeTags(request.Tags);
        note.UpdatedAt = DateTime.UtcNow;

        await Context.SaveChangesAsync();
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

        Context.UserNotes.Remove(note);
        await Context.SaveChangesAsync();
        return NoContent();
    }

    private static string NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return string.Empty;

        return string.Join(", ", tags
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }
}
