using AssetManagement.Data;
using AssetManagement.Dtos;
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
    private readonly PhotoService _photos;

    public MovementsController(
        AppDbContext context,
        IAuthSessionStore sessionStore,
        IAuditLogger auditLogger,
        PhotoService photos)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
        _photos = photos;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MovementCreateRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var quantityError = ValidateRequestedQuantity(request.Quantity);
        if (quantityError is not null)
            return BadRequest(new { message = quantityError });

        var createdPhotoFiles = new List<string>();
        var filesToDeleteAfterCommit = new List<string>();

        await using var transaction = await Context.Database.BeginTransactionAsync();

        try
        {
            await AcquireItemsWriteLockAsync();

            // Force a short write lock before reading the stock row so concurrent transfers are serialized.
            await TouchItemForWriteAsync(request.ItemId);

            var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == request.ItemId);
            if (item is null)
            {
                await transaction.RollbackAsync();
                return NotFound(new { message = "Item não encontrado." });
            }

            if (item.Quantity < request.Quantity)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "A quantidade não pode ser maior que o estoque atual do item." });
            }

            var fromLocation = (request.FromLocation ?? string.Empty).Trim();
            if (!string.Equals(fromLocation, item.Location.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "A origem precisa ser a localização atual do item." });
            }

            // Use the canonical casing stored in the database, regardless of what the client sent.
            fromLocation = item.Location.Trim();

            var fromCondition = item.Condition.Trim();
            var toCondition = (request.Condition ?? item.Condition).Trim();
            if (string.IsNullOrWhiteSpace(toCondition))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Informe o estado de conservação." });
            }

            var resolvedCondition = CatalogRules.ResolveCondition(toCondition);
            if (resolvedCondition is null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Estado de conservação inválido. Use uma das opções do sistema." });
            }

            toCondition = resolvedCondition;

            var conditionChanged = !string.Equals(fromCondition, toCondition, StringComparison.Ordinal);
            var fromIsDischarged = item.IsDischarged;
            var toIsDischarged = request.IsDischarged;
            var dischargeChanged = fromIsDischarged != toIsDischarged;
            var fromResponsiblePerson = NormalizeResponsiblePerson(item.ResponsiblePerson);
            var clearsResponsiblePerson = false;
            var destinationType = (request.DestinationType ?? string.Empty).Trim();
            var isLocalDestination = string.Equals(destinationType, "Local", StringComparison.OrdinalIgnoreCase);
            var isPersonDestination = string.Equals(destinationType, "Pessoa", StringComparison.OrdinalIgnoreCase);
            if (!isLocalDestination && !isPersonDestination)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Tipo de destino inválido." });
            }

            var toLocation = (request.ToLocation ?? string.Empty).Trim();
            if (isLocalDestination && string.IsNullOrWhiteSpace(toLocation))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Informe o destino físico da transferência." });
            }

            if (isLocalDestination)
            {
                var resolvedLocation = await ResolveKnownLocationAsync(toLocation);
                if (resolvedLocation is null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Localização inválida. Cadastre a localização antes de usar." });
                }

                toLocation = resolvedLocation;
            }

            var destination = isPersonDestination
                ? (request.DestinationPerson ?? string.Empty).Trim()
                : toLocation;

            if (!isLocalDestination && string.IsNullOrWhiteSpace(destination))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Informe o militar responsável pelo recebimento." });
            }

            var locationChanged = isLocalDestination
                && !string.Equals(fromLocation, toLocation, StringComparison.OrdinalIgnoreCase);
            var sameResponsiblePerson = isPersonDestination
                && !string.IsNullOrWhiteSpace(fromResponsiblePerson)
                && string.Equals(NormalizeResponsiblePerson(destination), fromResponsiblePerson, StringComparison.OrdinalIgnoreCase);
            if (isLocalDestination)
                clearsResponsiblePerson = !string.IsNullOrWhiteSpace(fromResponsiblePerson);
            var destinationChanged = isLocalDestination
                ? locationChanged || clearsResponsiblePerson
                : !sameResponsiblePerson;

            if (sameResponsiblePerson && !conditionChanged && !dischargeChanged)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Altere o responsável, a conservação ou a situação de baixa do item." });
            }

            if (isLocalDestination
                && string.Equals(toLocation, item.Location.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (!conditionChanged && !dischargeChanged && !clearsResponsiblePerson)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Altere o destino, a conservação ou a situação de baixa do item." });
                }
            }

            var now = DateTime.UtcNow;
            var movementItemId = item.Id;
            Item? destinationItem = null;

            var destinationLocation = isLocalDestination ? toLocation : item.Location;
            string? destinationResponsiblePerson = isLocalDestination ? null : destination;

            var matchingDestinationItem = await FindMatchingInventoryItemAsync(new ItemMatchCriteria(
                item.Name,
                item.AssetTag,
                item.Nature,
                destinationLocation,
                toCondition,
                item.Notes,
                toIsDischarged,
                destinationResponsiblePerson,
                item.Id));
            if (matchingDestinationItem is not null
                && ExceedsQuantityLimit(matchingDestinationItem.Quantity, request.Quantity))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "A quantidade total do item de destino excede o limite permitido." });
            }

            if (request.Quantity == item.Quantity)
            {
                if (matchingDestinationItem is not null)
                {
                    MergeQuantityAndPhotoIntoExistingItem(
                        item,
                        matchingDestinationItem,
                        item.Quantity,
                        toIsDischarged,
                        now,
                        filesToDeleteAfterCommit);
                    destinationItem = matchingDestinationItem;
                    await MergeItemAsync(item, matchingDestinationItem.Id);
                    movementItemId = matchingDestinationItem.Id;
                }
                else
                {
                    UpdateSourceItemDestination(item, destinationLocation, toCondition, destinationResponsiblePerson, toIsDischarged, now);
                    destinationItem = item;
                }
            }
            else
            {
                item.Quantity -= request.Quantity;
                item.UpdatedAt = now;

                if (matchingDestinationItem is not null)
                {
                    AddQuantityToExistingItem(matchingDestinationItem, request.Quantity, toIsDischarged, item.DischargedAt, now);
                    await CopyPhotoIfMissingAndTrackAsync(item, matchingDestinationItem, createdPhotoFiles);
                    destinationItem = matchingDestinationItem;
                }
                else
                {
                    destinationItem = await CreateSplitDestinationItemAsync(
                        item,
                        request.Quantity,
                        destinationLocation,
                        toCondition,
                        destinationResponsiblePerson,
                        toIsDischarged,
                        now,
                        createdPhotoFiles);
                }
            }

            await Context.SaveChangesAsync();

            var movement = new Movement
            {
                ItemId = movementItemId,
                DestinationItemId = destinationItem?.Id != movementItemId ? destinationItem?.Id : null,
                ItemName = item.Name,
                Quantity = request.Quantity,
                MovementType = "Transferência",
                FromLocation = fromLocation,
                ToLocation = toLocation,
                FromCondition = fromCondition,
                ToCondition = toCondition,
                FromIsDischarged = fromIsDischarged,
                ToIsDischarged = toIsDischarged,
                DestinationType = destinationType,
                DestinationPerson = destination,
                OriginPerson = fromResponsiblePerson,
                Notes = (request.Notes ?? string.Empty).Trim(),
                PerformedByUserId = currentUser.Id,
                PerformedByUserName = currentUser.Username,
                CreatedAt = now
            };

            Context.Movements.Add(movement);
            await Context.SaveChangesAsync();

            var auditDetails = new List<string>();
            if (destinationChanged)
                auditDetails.Add($"Origem: {movement.FromLocation}. Destino: {destination}");
            if (clearsResponsiblePerson)
                auditDetails.Add($"Responsável: {fromResponsiblePerson} -> (sem responsável)");
            if (conditionChanged)
                auditDetails.Add($"Estado: {movement.FromCondition} -> {movement.ToCondition}");
            if (dischargeChanged)
                auditDetails.Add($"Descargueado: {FormatBoolean(movement.FromIsDischarged)} -> {FormatBoolean(movement.ToIsDischarged)}");

            await _auditLogger.LogAsync(
                currentUser,
                AuditActions.Transfer,
                AuditEntityTypes.Movement,
                movement.Id.ToString(),
                $"{movement.ItemName} - {movement.Quantity} unidades",
                $"{string.Join(". ", auditDetails)}.");

            await transaction.CommitAsync();
            await _photos.DeletePhotoFilesAsync(filesToDeleteAfterCommit);
            return Ok(movement);
        }
        catch
        {
            await transaction.RollbackAsync();
            await _photos.DeletePhotoFilesAsync(createdPhotoFiles);
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao registrar a movimentação. Tente novamente." });
        }
    }

    private void MergeQuantityAndPhotoIntoExistingItem(
        Item sourceItem,
        Item destinationItem,
        int quantity,
        bool isDischarged,
        DateTime now,
        List<string> filesToDeleteAfterCommit)
    {
        AddQuantityToExistingItem(destinationItem, quantity, isDischarged, sourceItem.DischargedAt, now);
        TrackPhotoFile(
            _photos.MovePhotoReferenceOrReturnUnusedFile(sourceItem, destinationItem),
            filesToDeleteAfterCommit);
    }

    private static void AddQuantityToExistingItem(
        Item destinationItem,
        int quantity,
        bool isDischarged,
        DateTime? sourceDischargedAt,
        DateTime now)
    {
        destinationItem.Quantity += quantity;
        destinationItem.DischargedAt = ResolveDischargedAt(isDischarged, destinationItem.DischargedAt, sourceDischargedAt, now);
        destinationItem.UpdatedAt = now;
    }

    private static void UpdateSourceItemDestination(
        Item item,
        string location,
        string condition,
        string? responsiblePerson,
        bool isDischarged,
        DateTime now)
    {
        item.Location = location;
        item.Condition = condition;
        item.ResponsiblePerson = string.IsNullOrWhiteSpace(responsiblePerson) ? null : responsiblePerson.Trim();
        item.IsDischarged = isDischarged;
        item.DischargedAt = ResolveDischargedAt(isDischarged, item.DischargedAt, null, now);
        item.UpdatedAt = now;
    }

    private async Task<Item> CreateSplitDestinationItemAsync(
        Item sourceItem,
        int quantity,
        string location,
        string condition,
        string? responsiblePerson,
        bool isDischarged,
        DateTime now,
        List<string> createdPhotoFiles)
    {
        var destinationItem = new Item
        {
            Name = sourceItem.Name,
            Quantity = quantity,
            AssetTag = sourceItem.AssetTag,
            Nature = sourceItem.Nature,
            Location = location,
            Condition = condition,
            Notes = sourceItem.Notes,
            IsDischarged = isDischarged,
            DischargedAt = ResolveDischargedAt(isDischarged, null, sourceItem.DischargedAt, now),
            ResponsiblePerson = string.IsNullOrWhiteSpace(responsiblePerson) ? null : responsiblePerson.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        await CopyPhotoIfMissingAndTrackAsync(sourceItem, destinationItem, createdPhotoFiles);
        Context.Items.Add(destinationItem);
        return destinationItem;
    }

    private async Task CopyPhotoIfMissingAndTrackAsync(
        Item sourceItem,
        Item destinationItem,
        List<string> createdPhotoFiles)
    {
        var previousFileName = destinationItem.PhotoFileName;
        await _photos.CopyPhotoIfMissingAsync(sourceItem, destinationItem);

        if (string.IsNullOrWhiteSpace(previousFileName) && !string.IsNullOrWhiteSpace(destinationItem.PhotoFileName))
            TrackPhotoFile(destinationItem.PhotoFileName, createdPhotoFiles);
    }

    private static DateTime? ResolveDischargedAt(
        bool isDischarged,
        DateTime? currentDischargedAt,
        DateTime? sourceDischargedAt,
        DateTime now) =>
        isDischarged ? currentDischargedAt ?? sourceDischargedAt ?? now : null;

}
