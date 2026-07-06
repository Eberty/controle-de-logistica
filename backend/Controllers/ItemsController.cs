using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController : AuthenticatedControllerBase
{
    private const int MaxItemRequestBodyBytes = 3_000_000;

    private sealed record NormalizedItemRequest(
        string Name,
        string AssetTag,
        string Nature,
        string Location,
        string Condition,
        string Notes,
        bool IsDischarged);

    private readonly IAuditLogger _auditLogger;
    private readonly PhotoService _photos;

    public ItemsController(
        AppDbContext context,
        IAuthSessionStore sessionStore,
        IAuditLogger auditLogger,
        PhotoService photos)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
        _photos = photos;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var items = await Context.Items.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var item = await Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id)
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var item = await Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null || string.IsNullOrWhiteSpace(item.PhotoFileName))
            return NotFound();

        var photo = await _photos.OpenPhotoAsync(item.PhotoFileName, item.PhotoContentType);
        if (photo is null)
            return NotFound();

        return File(photo.Stream, photo.ContentType);
    }

    [HttpGet("{id:int}/movements")]
    public async Task<IActionResult> GetItemMovements(int id)
    {
        var (ok, _, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var itemExists = await Context.Items.AnyAsync(x => x.Id == id);
        if (!itemExists)
            return NotFound();

        var idStr = id.ToString();

        var movements = await Context.Movements.AsNoTracking()
            .Where(x => x.ItemId == id || x.DestinationItemId == id)
            .Select(m => new ItemHistoryEntryDto(
                "movement", m.Id, m.CreatedAt, m.PerformedByUserName,
                m.Quantity, m.FromLocation, m.ToLocation,
                m.FromCondition, m.ToCondition,
                m.FromIsDischarged, m.ToIsDischarged,
                m.DestinationType, m.DestinationPerson, m.OriginPerson, m.Notes,
                null, null))
            .ToListAsync();

        var auditEntries = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == AuditEntityTypes.Item && x.EntityId == idStr)
            .Select(a => new ItemHistoryEntryDto(
                "audit", a.Id, a.Timestamp, a.ActorUserName,
                null, null, null, null, null, null, null, null, null, null, null,
                a.Action, a.Details))
            .ToListAsync();

        var history = movements
            .Concat(auditEntries)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Ok(history);
    }

    [HttpPost]
    [RequestSizeLimit(MaxItemRequestBodyBytes)]
    public async Task<IActionResult> Create([FromBody] ItemUpsertRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var (normalizedItem, validationError) = await ValidateAndNormalizeItemAsync(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var now = DateTime.UtcNow;
        var (name, assetTag, nature, location, condition, notes, isDischarged) = normalizedItem!;

        var createdFields = new List<string>
        {
            $"Quantidade: {request.Quantity}",
            $"Natureza: {FormatAuditValue(nature)}",
            $"Localização: {FormatAuditValue(location)}",
            $"Estado: {FormatAuditValue(condition)}"
        };

        if (!string.IsNullOrWhiteSpace(assetTag))
            createdFields.Add($"Tombo: {FormatAuditValue(assetTag)}");

        if (!string.IsNullOrWhiteSpace(notes))
            createdFields.Add($"Observações: {FormatAuditValue(notes)}");

        if (isDischarged)
            createdFields.Add("Item descargueado: Sim");

        var item = new Item
        {
            Name = name,
            Quantity = request.Quantity,
            AssetTag = assetTag,
            Nature = nature,
            Location = location,
            Condition = condition,
            Notes = notes,
            IsDischarged = isDischarged,
            DischargedAt = isDischarged ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        UploadedPhoto? uploadedPhoto;
        try
        {
            uploadedPhoto = await _photos.UploadPhotoAsync(request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao salvar o item. Tente novamente." });
        }

        var createdPhotoFiles = new List<string>();
        TrackPhotoFile(uploadedPhoto?.FileName, createdPhotoFiles);
        var filesToDeleteAfterCommit = new List<string>();

        await using var transaction = await Context.Database.BeginTransactionAsync();
        try
        {
            await AcquireItemsWriteLockAsync();

            var existingItem = await FindMatchingInventoryItemAsync(new ItemMatchCriteria(
                name,
                assetTag,
                nature,
                location,
                condition,
                notes,
                isDischarged));
            if (existingItem is not null)
            {
                if (ExceedsQuantityLimit(existingItem.Quantity, request.Quantity))
                {
                    await transaction.RollbackAsync();
                    await DeletePhotoFilesAsync(createdPhotoFiles);
                    return BadRequest(new { message = "A quantidade total do item excede o limite permitido." });
                }

                var previousQuantity = existingItem.Quantity;
                existingItem.Name = name;
                existingItem.Quantity += request.Quantity;
                existingItem.UpdatedAt = now;
                TrackPhotoChange(
                    _photos.ApplyPhotoChange(existingItem, request, uploadedPhoto),
                    createdPhotoFiles,
                    filesToDeleteAfterCommit);
                await Context.SaveChangesAsync();

                var auditFields = new List<string>
                {
                    $"Quantidade somada no cadastro: {previousQuantity} -> {existingItem.Quantity}"
                };
                if (!string.IsNullOrWhiteSpace(request.PhotoDataUrl))
                    auditFields.Add("Foto: atualizada");

                await _auditLogger.LogAsync(
                    currentUser,
                    AuditActions.Update,
                    AuditEntityTypes.Item,
                    existingItem.Id.ToString(),
                    existingItem.Name,
                    $"{string.Join(" | ", auditFields)}.");
                await transaction.CommitAsync();
                await DeletePhotoFilesAsync(filesToDeleteAfterCommit);
                return Ok(existingItem);
            }

            TrackPhotoChange(
                _photos.ApplyPhotoChange(item, request, uploadedPhoto),
                createdPhotoFiles,
                filesToDeleteAfterCommit);

            if (!string.IsNullOrWhiteSpace(item.PhotoFileName))
                createdFields.Add("Foto: enviada");

            Context.Items.Add(item);
            await Context.SaveChangesAsync();

            await _auditLogger.LogAsync(
                currentUser,
                AuditActions.Create,
                AuditEntityTypes.Item,
                item.Id.ToString(),
                item.Name,
                createdFields.Count > 0
                    ? $"Campos cadastrados: {string.Join(" | ", createdFields)}."
                    : "Item cadastrado.");
            await transaction.CommitAsync();
            await DeletePhotoFilesAsync(filesToDeleteAfterCommit);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            await DeletePhotoFilesAsync(createdPhotoFiles);
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            await transaction.RollbackAsync();
            await DeletePhotoFilesAsync(createdPhotoFiles);
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao salvar o item. Tente novamente." });
        }

        return Ok(item);
    }

    [HttpPut("{id:int}")]
    [RequestSizeLimit(MaxItemRequestBodyBytes)]
    public async Task<IActionResult> Update(int id, [FromBody] ItemUpsertRequest request)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        var (normalizedItem, validationError) = await ValidateAndNormalizeItemAsync(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var (name, assetTag, nature, location, condition, notes, isDischarged) = normalizedItem!;

        var now = DateTime.UtcNow;

        UploadedPhoto? uploadedPhoto;
        try
        {
            uploadedPhoto = await _photos.UploadPhotoAsync(request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao atualizar o item. Tente novamente." });
        }

        var createdPhotoFiles = new List<string>();
        TrackPhotoFile(uploadedPhoto?.FileName, createdPhotoFiles);
        var filesToDeleteAfterCommit = new List<string>();

        await using var transaction = await Context.Database.BeginTransactionAsync();
        try
        {
            await AcquireItemsWriteLockAsync();

            var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == id);
            if (item is null)
            {
                await transaction.RollbackAsync();
                await DeletePhotoFilesAsync(createdPhotoFiles);
                return NotFound();
            }

            var changedFields = new List<string>();

            if (!string.Equals(item.Name, name, StringComparison.Ordinal))
                changedFields.Add($"Nome: {FormatAuditValue(item.Name)} -> {FormatAuditValue(name)}");

            if (item.Quantity != request.Quantity)
                changedFields.Add($"Quantidade: {item.Quantity} -> {request.Quantity}");

            if (!string.Equals(item.AssetTag, assetTag, StringComparison.Ordinal))
                changedFields.Add($"Tombo: {FormatAuditValue(item.AssetTag)} -> {FormatAuditValue(assetTag)}");

            if (!string.Equals(item.Nature, nature, StringComparison.Ordinal))
                changedFields.Add($"Natureza: {FormatAuditValue(item.Nature)} -> {FormatAuditValue(nature)}");

            if (!string.Equals(item.Location, location, StringComparison.Ordinal))
                changedFields.Add($"Localização: {FormatAuditValue(item.Location)} -> {FormatAuditValue(location)}");

            if (!string.Equals(item.Condition, condition, StringComparison.Ordinal))
                changedFields.Add($"Estado: {FormatAuditValue(item.Condition)} -> {FormatAuditValue(condition)}");

            if (!string.Equals(item.Notes, notes, StringComparison.Ordinal))
                changedFields.Add($"Observações: {FormatAuditValue(item.Notes)} -> {FormatAuditValue(notes)}");

            if (item.IsDischarged != isDischarged)
                changedFields.Add($"Item descargueado: {FormatBoolean(item.IsDischarged)} -> {FormatBoolean(isDischarged)}");

            if (!string.IsNullOrWhiteSpace(request.PhotoDataUrl))
                changedFields.Add("Foto: atualizada");
            else if (request.RemovePhoto && !string.IsNullOrWhiteSpace(item.PhotoFileName))
                changedFields.Add("Foto: removida");

            if (changedFields.Count == 0)
            {
                await transaction.CommitAsync();
                await DeletePhotoFilesAsync(createdPhotoFiles);
                return Ok(item);
            }

            var existingItem = await FindMatchingInventoryItemAsync(new ItemMatchCriteria(
                name,
                assetTag,
                nature,
                location,
                condition,
                notes,
                isDischarged,
                item.ResponsiblePerson,
                item.Id));

            if (existingItem is not null)
            {
                if (ExceedsQuantityLimit(existingItem.Quantity, request.Quantity))
                {
                    await transaction.RollbackAsync();
                    await DeletePhotoFilesAsync(createdPhotoFiles);
                    return BadRequest(new { message = "A quantidade total do item excede o limite permitido." });
                }

                var previousQuantity = existingItem.Quantity;
                existingItem.Name = name;
                existingItem.Quantity += request.Quantity;
                existingItem.DischargedAt = isDischarged
                    ? existingItem.DischargedAt ?? item.DischargedAt ?? now
                    : null;
                existingItem.UpdatedAt = now;

                if (uploadedPhoto is not null || request.RemovePhoto)
                {
                    TrackPhotoChange(
                        _photos.ApplyPhotoChange(existingItem, request, uploadedPhoto),
                        createdPhotoFiles,
                        filesToDeleteAfterCommit);
                    TrackPhotoFile(item.PhotoFileName, filesToDeleteAfterCommit);
                }
                else
                {
                    TrackPhotoFile(
                        _photos.MovePhotoReferenceOrReturnUnusedFile(item, existingItem),
                        filesToDeleteAfterCommit);
                }

                await MergeItemAsync(item, existingItem.Id);
                await Context.SaveChangesAsync();

                var mergeFields = new List<string>(changedFields)
                {
                    $"Quantidade mesclada: {previousQuantity} -> {existingItem.Quantity}"
                };

                await _auditLogger.LogAsync(
                    currentUser,
                    AuditActions.Update,
                    AuditEntityTypes.Item,
                    existingItem.Id.ToString(),
                    existingItem.Name,
                    $"Campos editados: {string.Join(" | ", mergeFields)}.");
                await transaction.CommitAsync();
                await DeletePhotoFilesAsync(filesToDeleteAfterCommit);
                return Ok(existingItem);
            }

            item.Name = name;
            item.Quantity = request.Quantity;
            item.AssetTag = assetTag;
            item.Nature = nature;
            item.Location = location;
            item.Condition = condition;
            item.Notes = notes;
            item.IsDischarged = isDischarged;
            item.DischargedAt = isDischarged
                ? item.DischargedAt ?? now
                : null;
            item.UpdatedAt = now;

            TrackPhotoChange(
                _photos.ApplyPhotoChange(item, request, uploadedPhoto),
                createdPhotoFiles,
                filesToDeleteAfterCommit);
            await Context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                currentUser,
                AuditActions.Update,
                AuditEntityTypes.Item,
                item.Id.ToString(),
                item.Name,
                $"Campos editados: {string.Join(" | ", changedFields)}.");
            await transaction.CommitAsync();
            await DeletePhotoFilesAsync(filesToDeleteAfterCommit);
            return Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            await DeletePhotoFilesAsync(createdPhotoFiles);
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            await transaction.RollbackAsync();
            await DeletePhotoFilesAsync(createdPhotoFiles);
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao atualizar o item. Tente novamente." });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        await using var transaction = await Context.Database.BeginTransactionAsync();

        await AcquireItemsWriteLockAsync();

        var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            await transaction.RollbackAsync();
            return NotFound();
        }

        var idStr = item.Id.ToString();

        await Context.Movements
            .Where(x => x.ItemId == id && x.DestinationItemId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ItemId, x => x.DestinationItemId!.Value)
                .SetProperty(x => x.DestinationItemId, (int?)null));
        await Context.Movements
            .Where(x => x.DestinationItemId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DestinationItemId, (int?)null));
        await Context.Movements
            .Where(x => x.ItemId == id)
            .ExecuteDeleteAsync();

        Context.Items.Remove(item);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Delete,
            AuditEntityTypes.Item,
            idStr,
            item.Name,
            "Item removido.");
        await transaction.CommitAsync();
        await DeletePhotoFilesAsync(new[] { item.PhotoFileName });

        return NoContent();
    }

    private static void TrackPhotoChange(
        PhotoChangeResult change,
        List<string> createdPhotoFiles,
        List<string> filesToDeleteAfterCommit)
    {
        TrackPhotoFile(change.CreatedFileName, createdPhotoFiles);
        TrackPhotoFile(change.ReplacedFileName, filesToDeleteAfterCommit);
    }

    private Task DeletePhotoFilesAsync(IEnumerable<string> fileNames) =>
        _photos.DeletePhotoFilesAsync(fileNames);

    private async Task<(NormalizedItemRequest? Item, string? ErrorMessage)> ValidateAndNormalizeItemAsync(ItemUpsertRequest request)
    {
        if (!IsNumericAssetTag(request.AssetTag))
            return (null, "O tombo deve conter apenas números.");

        var quantityError = ValidateRequestedQuantity(request.Quantity);
        if (quantityError is not null)
            return (null, quantityError);

        var item = NormalizeItemFields(request);

        if (string.IsNullOrWhiteSpace(item.Name)
            || string.IsNullOrWhiteSpace(item.Nature)
            || string.IsNullOrWhiteSpace(item.Location)
            || string.IsNullOrWhiteSpace(item.Condition))
            return (null, "Preencha os campos obrigatórios do item.");

        var resolvedNature = CatalogRules.ResolveNature(item.Nature);
        if (resolvedNature is null)
            return (null, "Natureza inválida. Use uma das opções do sistema.");

        var resolvedCondition = CatalogRules.ResolveCondition(item.Condition);
        if (resolvedCondition is null)
            return (null, "Estado de conservação inválido. Use uma das opções do sistema.");

        var resolvedLocation = await ResolveKnownLocationAsync(item.Location);
        if (resolvedLocation is null)
            return (null, "Localização inválida. Cadastre a localização antes de usar.");

        return (item with
        {
            Nature = resolvedNature,
            Location = resolvedLocation,
            Condition = resolvedCondition
        }, null);
    }

    private static NormalizedItemRequest NormalizeItemFields(ItemUpsertRequest request) => new(
        FormatItemName(request.Name),
        (request.AssetTag ?? string.Empty).Trim(),
        (request.Nature ?? string.Empty).Trim(),
        (request.Location ?? string.Empty).Trim(),
        (request.Condition ?? string.Empty).Trim(),
        (request.Notes ?? string.Empty).Trim(),
        request.IsDischarged);

    private static bool IsNumericAssetTag(string? value)
    {
        var trimmedValue = (value ?? string.Empty).Trim();
        return trimmedValue.Length == 0 || trimmedValue.All(char.IsDigit);
    }

    private static string FormatItemName(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

}
