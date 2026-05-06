using AssetManagement.Data;
using AssetManagement.DTOs;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController : AuthenticatedControllerBase
{
    private readonly IAuditLogger _auditLogger;
    private readonly IWebHostEnvironment _environment;

    public ItemsController(
        AppDbContext context,
        IAuthSessionStore sessionStore,
        IAuditLogger auditLogger,
        IWebHostEnvironment environment)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetCurrentUser(out _, out var error))
            return error!;

        var items = await Context.Items.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetCurrentUser(out _, out var error))
            return error!;

        var item = await Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id)
    {
        if (!TryGetCurrentUser(out _, out var error))
            return error!;

        var item = await Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null || string.IsNullOrWhiteSpace(item.PhotoFileName))
            return NotFound();

        var photoPath = GetPhotoPath(item.PhotoFileName);
        if (!System.IO.File.Exists(photoPath))
            return NotFound();

        return PhysicalFile(photoPath, item.PhotoContentType);
    }

    [HttpGet("{id:int}/movements")]
    public async Task<IActionResult> GetItemMovements(int id)
    {
        if (!TryGetCurrentUser(out _, out var error))
            return error!;

        var itemExists = await Context.Items.AnyAsync(x => x.Id == id);
        if (!itemExists)
            return NotFound();

        var movements = await Context.Movements.AsNoTracking()
            .Where(x => x.ItemId == id)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return Ok(movements);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ItemUpsertRequest request)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        if (!IsNumericAssetTag(request.AssetTag))
            return BadRequest(new { message = "O número de patrimônio deve conter apenas números." });

        if (request.Quantity < 0)
            return BadRequest(new { message = "A quantidade não pode ser negativa." });

        var now = DateTime.UtcNow;
        var item = new Item
        {
            Name = FormatItemName(request.Name),
            Quantity = request.Quantity,
            AssetTag = request.AssetTag.Trim(),
            Nature = request.Nature.Trim(),
            Location = request.Location.Trim(),
            Condition = request.Condition.Trim(),
            Notes = request.Notes.Trim(),
            IsDischarged = request.IsDischarged,
            DischargedAt = request.IsDischarged ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var createdFields = new List<string>
        {
            $"Quantidade: {item.Quantity}",
            $"Natureza: {FormatAuditValue(item.Nature)}",
            $"Localização: {FormatAuditValue(item.Location)}",
            $"Estado: {FormatAuditValue(item.Condition)}"
        };

        if (!string.IsNullOrWhiteSpace(item.AssetTag))
            createdFields.Add($"Número de patrimônio: {FormatAuditValue(item.AssetTag)}");

        if (!string.IsNullOrWhiteSpace(item.Notes))
            createdFields.Add($"Observações: {FormatAuditValue(item.Notes)}");

        if (item.IsDischarged)
            createdFields.Add("Item descargueado: Sim");

        await using var transaction = await Context.Database.BeginTransactionAsync();
        try
        {
            Context.Items.Add(item);
            await Context.SaveChangesAsync();

            await ApplyPhotoChangeAsync(item, request);

            if (!string.IsNullOrWhiteSpace(item.PhotoFileName))
            {
                createdFields.Add("Foto: enviada");
                await Context.SaveChangesAsync();
            }

            await _auditLogger.LogAsync(
                currentUser,
                "Criação",
                "Item",
                item.Id.ToString(),
                item.Name,
                createdFields.Count > 0
                    ? $"Campos cadastrados: {string.Join(" | ", createdFields)}."
                    : "Item cadastrado.");
            await transaction.CommitAsync();
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            DeletePhotoFile(item.PhotoFileName);
            return BadRequest(new { message = ex.Message });
        }

        return Ok(item);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ItemUpsertRequest request)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        if (!IsNumericAssetTag(request.AssetTag))
            return BadRequest(new { message = "O número de patrimônio deve conter apenas números." });

        if (request.Quantity < 0)
            return BadRequest(new { message = "A quantidade não pode ser negativa." });

        var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
            return NotFound();

        var name = FormatItemName(request.Name);
        var assetTag = request.AssetTag.Trim();
        var nature = request.Nature.Trim();
        var location = request.Location.Trim();
        var condition = request.Condition.Trim();
        var notes = request.Notes.Trim();
        var isDischarged = request.IsDischarged;
        var changedFields = new List<string>();

        if (!string.Equals(item.Name, name, StringComparison.Ordinal))
            changedFields.Add($"Nome: {FormatAuditValue(item.Name)} -> {FormatAuditValue(name)}");

        if (item.Quantity != request.Quantity)
            changedFields.Add($"Quantidade: {item.Quantity} -> {request.Quantity}");

        if (!string.Equals(item.AssetTag, assetTag, StringComparison.Ordinal))
            changedFields.Add($"Número de patrimônio: {FormatAuditValue(item.AssetTag)} -> {FormatAuditValue(assetTag)}");

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

        var now = DateTime.UtcNow;
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

        try
        {
            await ApplyPhotoChangeAsync(item, request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            "Atualização",
            "Item",
            item.Id.ToString(),
            item.Name,
            changedFields.Count > 0
                ? $"Campos editados: {string.Join(" | ", changedFields)}."
                : "Item editado.");

        return Ok(item);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        var item = await Context.Items.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
            return NotFound();

        DeletePhotoFile(item.PhotoFileName);
        Context.Items.Remove(item);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            "Exclusão",
            "Item",
            item.Id.ToString(),
            item.Name,
            "Item removido.");

        return NoContent();
    }

    private static bool IsNumericAssetTag(string value)
    {
        var trimmedValue = value.Trim();
        return trimmedValue.Length == 0 || trimmedValue.All(char.IsDigit);
    }

    private static string FormatItemName(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string FormatAuditValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(vazio)" : value;
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "Sim" : "Não";
    }

    private async Task ApplyPhotoChangeAsync(Item item, ItemUpsertRequest request)
    {
        var hasNewPhoto = !string.IsNullOrWhiteSpace(request.PhotoDataUrl);
        var oldPhotoFileName = item.PhotoFileName;
        (byte[] Bytes, string ContentType, string Extension)? photo = hasNewPhoto
            ? DecodePhotoDataUrl(request.PhotoDataUrl!)
            : null;

        if (request.RemovePhoto || hasNewPhoto)
        {
            item.PhotoFileName = string.Empty;
            item.PhotoContentType = string.Empty;
        }

        if (!hasNewPhoto || photo is null)
        {
            if (request.RemovePhoto)
                DeletePhotoFile(oldPhotoFileName);

            return;
        }

        var fileName = $"{item.Id}-{Guid.NewGuid():N}{photo.Value.Extension}";
        var photoPath = GetPhotoPath(fileName);

        Directory.CreateDirectory(GetPhotoDirectory());
        await System.IO.File.WriteAllBytesAsync(photoPath, photo.Value.Bytes);

        item.PhotoFileName = fileName;
        item.PhotoContentType = photo.Value.ContentType;
        DeletePhotoFile(oldPhotoFileName);
    }

    private static (byte[] Bytes, string ContentType, string Extension) DecodePhotoDataUrl(string dataUrl)
    {
        const string marker = ";base64,";
        var markerIndex = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (!dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || markerIndex < 0)
            throw new InvalidOperationException("A foto enviada é inválida.");

        var contentType = dataUrl[5..markerIndex].ToLowerInvariant();
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Use uma foto em JPG, PNG ou WebP.")
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(markerIndex + marker.Length)..]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("A foto enviada é inválida.");
        }

        if (bytes.Length > 1_500_000)
            throw new InvalidOperationException("A foto ficou grande demais. Escolha uma imagem menor.");

        return (bytes, contentType, extension);
    }

    private string GetPhotoDirectory()
    {
        return Path.Combine(_environment.ContentRootPath, "Data", "images");
    }

    private string GetPhotoPath(string fileName)
    {
        return Path.Combine(GetPhotoDirectory(), Path.GetFileName(fileName));
    }

    private void DeletePhotoFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var photoPath = GetPhotoPath(fileName);
        if (System.IO.File.Exists(photoPath))
            System.IO.File.Delete(photoPath);
    }
}
