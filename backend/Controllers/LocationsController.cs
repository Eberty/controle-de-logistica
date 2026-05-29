using AssetManagement.Data;
using AssetManagement.Dtos;
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

    private static LocationOption? FindLocation(IEnumerable<LocationOption> locations, string name, int? excludeId = null) =>
        locations.FirstOrDefault(x =>
            (excludeId == null || x.Id != excludeId)
            && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var locations = await Context.LocationOptions.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync();

        return Ok(locations);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LocationCreateRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Informe o nome da localização." });

        if (CatalogRules.ResolveDefaultLocation(name) is not null)
            return Conflict(new { message = "Essa localização já existe." });

        var allLocations = await Context.LocationOptions.AsNoTracking().ToListAsync();
        var existingLocation = FindLocation(allLocations, name);

        if (existingLocation is not null)
            return Conflict(new { message = "Essa localização já existe." });

        var location = new LocationOption
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.LocationOptions.Add(location);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            "Criação",
            "Localização",
            location.Id.ToString(),
            location.Name,
            "Localização cadastrada.");
        await transaction.CommitAsync();
        return Created($"/api/locations/{location.Id}", location.Name);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] LocationUpdateRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var currentName = request.CurrentName?.Trim() ?? "";
        var newName = request.NewName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(newName))
            return BadRequest(new { message = "Informe a localização atual e o novo nome." });

        await using var transaction = await Context.Database.BeginTransactionAsync();
        try
        {
            await AcquireItemsWriteLockAsync();

            var allLocations = await Context.LocationOptions.ToListAsync();
            var location = FindLocation(allLocations, currentName);

            if (location is null)
            {
                await transaction.RollbackAsync();
                return NotFound(new { message = "Localização não encontrada." });
            }

            if (string.Equals(location.Name, newName, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync();
                return Ok(location.Name);
            }

            if (CatalogRules.ResolveDefaultLocation(newName) is not null)
            {
                await transaction.RollbackAsync();
                return Conflict(new { message = "Essa localização já existe." });
            }

            var duplicateLocation = FindLocation(allLocations, newName, location.Id);

            if (duplicateLocation is not null)
            {
                await transaction.RollbackAsync();
                return Conflict(new { message = "Essa localização já existe." });
            }

            var oldName = location.Name;
            var itemsWithLocation = await QueryItemsByLocation(oldName).ToListAsync();

            var now = DateTime.UtcNow;
            location.Name = newName;
            foreach (var item in itemsWithLocation)
            {
                item.Location = newName;
                item.UpdatedAt = now;
            }

            await Context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                currentUser,
                "Atualização",
                "Localização",
                location.Id.ToString(),
                location.Name,
                $"Localização editada: {oldName} -> {location.Name}.");
            await transaction.CommitAsync();
            return Ok(location.Name);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] LocationDeleteRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Informe a localização." });

        await using var transaction = await Context.Database.BeginTransactionAsync();

        await AcquireItemsWriteLockAsync();

        var allLocations = await Context.LocationOptions.ToListAsync();
        var location = FindLocation(allLocations, name);

        if (location is null)
        {
            await transaction.RollbackAsync();
            return NotFound(new { message = "Localização não encontrada." });
        }

        var locationInUse = await QueryItemsByLocation(location.Name).AsNoTracking().AnyAsync();

        if (locationInUse)
        {
            await transaction.RollbackAsync();
            return Conflict(new { message = "Localização em uso. Não pode ser removida." });
        }

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
        await transaction.CommitAsync();

        return NoContent();
    }

}
