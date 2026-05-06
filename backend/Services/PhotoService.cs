using AssetManagement.Models;
using Microsoft.AspNetCore.Hosting;

namespace AssetManagement.Services;

public sealed record PhotoChangeResult(string CreatedFileName = "", string ReplacedFileName = "");

public class PhotoService
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpgSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] WebpRiffSignature = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
    private static readonly byte[] WebpFormatSignature = { 0x57, 0x45, 0x42, 0x50 }; // "WEBP"

    private readonly IWebHostEnvironment _environment;

    public PhotoService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public string GetPhotoDirectory()
    {
        return Path.Combine(_environment.ContentRootPath, "Data", "images");
    }

    public string GetPhotoPath(string fileName)
    {
        return Path.Combine(GetPhotoDirectory(), Path.GetFileName(fileName));
    }

    private void DeletePhotoFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var path = GetPhotoPath(fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void DeletePhotoFiles(IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            try
            {
                DeletePhotoFile(fileName);
            }
            catch (IOException)
            {
                // Cleanup failures should not undo an already committed change.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup failures should not undo an already committed change.
            }
        }
    }

    public string MovePhotoReferenceOrReturnUnusedFile(Item sourceItem, Item targetItem)
    {
        if (string.IsNullOrWhiteSpace(sourceItem.PhotoFileName))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(targetItem.PhotoFileName))
        {
            targetItem.PhotoFileName = sourceItem.PhotoFileName;
            targetItem.PhotoContentType = sourceItem.PhotoContentType;
            return string.Empty;
        }

        return sourceItem.PhotoFileName;
    }

    public async Task CopyPhotoIfMissingAsync(Item sourceItem, Item targetItem)
    {
        if (!string.IsNullOrWhiteSpace(targetItem.PhotoFileName) || string.IsNullOrWhiteSpace(sourceItem.PhotoFileName))
            return;

        var sourcePath = GetPhotoPath(sourceItem.PhotoFileName);
        if (!File.Exists(sourcePath))
            return;

        Directory.CreateDirectory(GetPhotoDirectory());

        var extension = Path.GetExtension(sourceItem.PhotoFileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var targetPath = GetPhotoPath(fileName);

        await using (var sourceStream = File.OpenRead(sourcePath))
        await using (var targetStream = File.Create(targetPath))
        {
            await sourceStream.CopyToAsync(targetStream);
        }

        targetItem.PhotoFileName = fileName;
        targetItem.PhotoContentType = sourceItem.PhotoContentType;
    }

    public async Task<PhotoChangeResult> ApplyPhotoChangeAsync(Item item, Dtos.ItemUpsertRequest request)
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
            return request.RemovePhoto
                ? new PhotoChangeResult(ReplacedFileName: oldPhotoFileName)
                : new PhotoChangeResult();
        }

        var fileName = $"{item.Id}-{Guid.NewGuid():N}{photo.Value.Extension}";
        var photoPath = GetPhotoPath(fileName);

        Directory.CreateDirectory(GetPhotoDirectory());
        await File.WriteAllBytesAsync(photoPath, photo.Value.Bytes);

        item.PhotoFileName = fileName;
        item.PhotoContentType = photo.Value.ContentType;
        return new PhotoChangeResult(fileName, oldPhotoFileName);
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

        if (!HasExpectedImageSignature(bytes, contentType))
            throw new InvalidOperationException("A foto enviada é inválida.");

        return (bytes, contentType, extension);
    }

    private static bool HasExpectedImageSignature(byte[] bytes, string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => bytes.AsSpan().StartsWith(JpgSignature),
            "image/png" => bytes.AsSpan().StartsWith(PngSignature),
            "image/webp" => bytes.Length >= 12
                && bytes.AsSpan(0, 4).SequenceEqual(WebpRiffSignature)
                && bytes.AsSpan(8, 4).SequenceEqual(WebpFormatSignature),
            _ => false
        };
    }
}
