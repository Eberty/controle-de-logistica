using System.Globalization;
using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/calendar")]
public class CalendarController : AuthenticatedControllerBase
{
    private const int MaxEntries = 5000;

    private readonly IAuditLogger _auditLogger;

    public CalendarController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var rows = await Context.CalendarEntries
            .AsNoTracking()
            .GroupJoin(
                Context.Users.AsNoTracking(),
                entry => entry.CreatedByUserId,
                user => user.Id,
                (entry, users) => new { entry, users })
            .SelectMany(
                x => x.users.DefaultIfEmpty(),
                (x, user) => new { x.entry, user })
            .OrderByDescending(x => x.entry.DueDate)
            .ThenByDescending(x => x.entry.Id)
            .Select(x => new
            {
                x.entry,
                AuthorFullName = x.user == null ? null : x.user.FullName,
                AuthorUsername = x.user == null ? null : x.user.Username
            })
            .Take(MaxEntries)
            .ToListAsync();

        var entries = Enumerable.Reverse(rows)
            .Select(x => new CalendarEntryDto(
            x.entry.Id,
            x.entry.DueDate,
            x.entry.SeiNumber,
            x.entry.Subject,
            x.entry.Notes,
            x.entry.CreatedByUserId,
            x.AuthorUsername == null
                ? x.entry.CreatedByUserName
                : DisplayNameFrom(x.AuthorFullName, x.AuthorUsername),
            x.entry.CreatedAt,
            x.entry.UpdatedAt));

        return Ok(entries);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CalendarEntryRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var (normalized, validationError) = ValidateAndNormalize(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var now = DateTime.UtcNow;
        var entry = new CalendarEntry
        {
            DueDate = normalized!.DueDate,
            SeiNumber = normalized.SeiNumber,
            Subject = normalized.Subject,
            Notes = normalized.Notes,
            CreatedByUserId = currentUser.Id,
            CreatedByUserName = UserDisplayName(currentUser),
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.CalendarEntries.Add(entry);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Create,
            AuditEntityTypes.Calendar,
            entry.Id.ToString(),
            entry.Subject,
            $"Anotação criada para {FormatDate(entry.DueDate)}.");
        await transaction.CommitAsync();

        return Created($"/api/calendar/{entry.Id}", ToDto(entry, UserDisplayName(currentUser)));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CalendarEntryRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var entry = await Context.CalendarEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (!CanManage(currentUser, entry))
            return StatusCode(403, new { message = "Apenas quem criou a anotação pode alterá-la." });

        var (normalized, validationError) = ValidateAndNormalize(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        entry.DueDate = normalized!.DueDate;
        entry.SeiNumber = normalized.SeiNumber;
        entry.Subject = normalized.Subject;
        entry.Notes = normalized.Notes;
        entry.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync();
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Update,
            AuditEntityTypes.Calendar,
            entry.Id.ToString(),
            entry.Subject,
            $"Anotação editada para {FormatDate(entry.DueDate)}.");
        await transaction.CommitAsync();

        return Ok(ToDto(entry, await ResolveAuthorNameAsync(entry, currentUser)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var entry = await Context.CalendarEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
            return NotFound(new { message = "Anotação não encontrada." });

        if (!CanManage(currentUser, entry))
            return StatusCode(403, new { message = "Apenas quem criou a anotação pode excluí-la." });

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.CalendarEntries.Remove(entry);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Delete,
            AuditEntityTypes.Calendar,
            id.ToString(),
            entry.Subject,
            "Anotação removida.");
        await transaction.CommitAsync();

        return NoContent();
    }

    private static bool CanManage(User user, CalendarEntry entry) =>
        entry.CreatedByUserId == user.Id || IsAdmin(user);

    private async Task<string> ResolveAuthorNameAsync(CalendarEntry entry, User currentUser)
    {
        if (entry.CreatedByUserId == currentUser.Id)
            return UserDisplayName(currentUser);

        var author = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entry.CreatedByUserId);
        return author is null ? entry.CreatedByUserName : UserDisplayName(author);
    }

    private static (CalendarEntry? Entry, string? Error) ValidateAndNormalize(CalendarEntryRequest request)
    {
        var dueDate = (request.DueDate ?? string.Empty).Trim();
        var subject = (request.Subject ?? string.Empty).Trim();

        if (!DateOnly.TryParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return (null, "Informe uma data válida.");

        if (string.IsNullOrWhiteSpace(subject))
            return (null, "Informe o assunto da anotação.");

        return (new CalendarEntry
        {
            DueDate = dueDate,
            SeiNumber = (request.SeiNumber ?? string.Empty).Trim(),
            Subject = subject,
            Notes = (request.Notes ?? string.Empty).Trim()
        }, null);
    }

    private static CalendarEntryDto ToDto(CalendarEntry entry, string authorName) => new(
        entry.Id,
        entry.DueDate,
        entry.SeiNumber,
        entry.Subject,
        entry.Notes,
        entry.CreatedByUserId,
        authorName,
        entry.CreatedAt,
        entry.UpdatedAt);

    private static string FormatDate(string isoDate) =>
        DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : isoDate;
}
