using AssetManagement.Data;
using AssetManagement.DTOs;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/movements")]
public class MovementsController : AuthenticatedControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public MovementsController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MovementCreateRequest request)
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == request.ItemId);
        if (item is null)
            return NotFound(new { message = "Item não encontrado." });

        if (request.Quantity <= 0)
            return BadRequest(new { message = "A quantidade deve ser maior que zero." });

        if (item.Quantity < request.Quantity)
            return BadRequest(new { message = "A quantidade não pode ser maior que o estoque atual do item." });

        if (!string.Equals(request.FromLocation.Trim(), item.Location.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "A origem precisa ser a localização atual do item." });

        if (string.Equals(request.DestinationType, "Local", StringComparison.OrdinalIgnoreCase)
            && string.Equals((request.ToLocation ?? string.Empty).Trim(), item.Location.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "O destino precisa ser diferente da origem." });

        if (string.Equals(request.DestinationType, "Local", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.ToLocation))
        {
            item.Location = request.ToLocation.Trim();
        }

        item.UpdatedAt = DateTime.UtcNow;

        var movement = new Movement
        {
            ItemId = item.Id,
            ItemName = item.Name,
            Quantity = request.Quantity,
            MovementType = "Transfer",
            FromLocation = request.FromLocation.Trim(),
            ToLocation = (request.ToLocation ?? string.Empty).Trim(),
            DestinationType = request.DestinationType.Trim(),
            DestinationPerson = (request.DestinationPerson ?? string.Empty).Trim(),
            OriginPerson = currentUser.FullName,
            Notes = (request.Notes ?? string.Empty).Trim(),
            PerformedByUserId = currentUser.Id,
            PerformedByUserName = currentUser.FullName,
            CreatedAt = DateTime.UtcNow
        };

        Context.Movements.Add(movement);
        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Transferência",
            "Movimentação",
            movement.Id.ToString(),
            $"{movement.ItemName} - {movement.Quantity} unidades",
            $"Origem: {movement.FromLocation}. Destino: {(string.Equals(movement.DestinationType, "Pessoa", StringComparison.OrdinalIgnoreCase) ? movement.DestinationPerson : movement.ToLocation)}.");

        return Ok(movement);
    }
}
