using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/mural")]
public class MuralController : AuthenticatedControllerBase
{
    private const int MaxNotes = 1000;

    public MuralController(AppDbContext context, IAuthSessionStore sessionStore)
        : base(context, sessionStore)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var rows = await Context.UserNotes
            .AsNoTracking()
            .Where(x => x.IsPublic)
            .Join(
                Context.Users.AsNoTracking(),
                note => note.UserId,
                user => user.Id,
                (note, user) => new { note, user })
            .OrderByDescending(x => x.note.UpdatedAt)
            .Select(x => new
            {
                x.note,
                x.user.FullName,
                x.user.Username
            })
            .Take(MaxNotes)
            .ToListAsync();

        var notes = rows.Select(x => new MuralNoteDto(
            x.note.Id,
            x.note.Title,
            x.note.Content,
            x.note.Tags,
            x.note.UserId,
            DisplayNameFrom(x.FullName, x.Username),
            x.note.CreatedAt,
            x.note.UpdatedAt));

        return Ok(notes);
    }
}
