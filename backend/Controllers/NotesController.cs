using AssetManagement.Data;
using AssetManagement.DTOs;
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
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

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
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var now = DateTime.UtcNow;
        var note = new UserNote
        {
            UserId = currentUser.Id,
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Tags = (request.Tags ?? string.Empty).Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        Context.UserNotes.Add(note);
        await Context.SaveChangesAsync();
        return Ok(note);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserNoteRequest request)
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.Id);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        note.Title = request.Title.Trim();
        note.Content = request.Content.Trim();
        note.Tags = (request.Tags ?? string.Empty).Trim();
        note.UpdatedAt = DateTime.UtcNow;

        await Context.SaveChangesAsync();
        return Ok(note);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var note = await Context.UserNotes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUser.Id);
        if (note is null)
            return NotFound(new { message = "Anotação não encontrada." });

        Context.UserNotes.Remove(note);
        await Context.SaveChangesAsync();
        return NoContent();
    }
}
