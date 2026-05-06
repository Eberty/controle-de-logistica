using AssetManagement.Data;
using AssetManagement.DTOs;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController : AuthenticatedControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public LocationsController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetCurrentUser(out _, out var error))
            return error!;

        var locations = await Context.LocationOptions.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync();

        return Ok(locations);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LocationCreateRequest request)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Informe o nome da localização." });

        var normalizedName = name.ToLower();
        var existingLocation = await Context.LocationOptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedName);

        if (existingLocation is not null)
            return Conflict(new { message = "Essa localização já existe." });

        var location = new LocationOption
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        Context.LocationOptions.Add(location);
        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Criação",
            "Localização",
            location.Id.ToString(),
            location.Name,
            "Localização cadastrada.");
        return Created($"/api/locations/{location.Id}", location.Name);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] LocationUpdateRequest request)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        var currentName = request.CurrentName?.Trim() ?? "";
        var newName = request.NewName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(newName))
            return BadRequest(new { message = "Informe a localização atual e o novo nome." });

        var currentNormalizedName = currentName.ToLower();
        var newNormalizedName = newName.ToLower();

        var location = await Context.LocationOptions
            .FirstOrDefaultAsync(x => x.Name.ToLower() == currentNormalizedName);

        if (location is null)
            return NotFound(new { message = "Localização não encontrada." });

        if (string.Equals(location.Name, newName, StringComparison.Ordinal))
            return Ok(location.Name);

        var duplicateLocation = await Context.LocationOptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == newNormalizedName && x.Id != location.Id);

        if (duplicateLocation is not null)
            return Conflict(new { message = "Essa localização já existe." });

        var oldName = location.Name;
        var itemsWithLocation = await Context.Items
            .Where(x => x.Location.ToLower() == currentNormalizedName)
            .ToListAsync();

        location.Name = newName;
        foreach (var item in itemsWithLocation)
        {
            item.Location = newName;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Atualização",
            "Localização",
            location.Id.ToString(),
            location.Name,
            $"Localização editada: {oldName} -> {location.Name}.");

        return Ok(location.Name);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] LocationDeleteRequest request)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Informe a localização." });

        var normalizedName = name.ToLower();
        var location = await Context.LocationOptions
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedName);

        if (location is null)
            return NotFound(new { message = "Localização não encontrada." });

        var locationInUse = await Context.Items.AsNoTracking()
            .AnyAsync(x => x.Location.ToLower() == normalizedName);

        if (locationInUse)
            return Conflict(new { message = "Localização em uso. Não pode ser removida." });

        var locationId = location.Id.ToString();
        var locationName = location.Name;

        Context.LocationOptions.Remove(location);
        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Exclusão",
            "Localização",
            locationId,
            locationName,
            "Localização removida.");

        return NoContent();
    }

}
