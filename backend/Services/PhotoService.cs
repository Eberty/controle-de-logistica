using AssetManagement.Models;

namespace AssetManagement.Services;

public sealed record PhotoChangeResult(string CreatedFileName = "", string ReplacedFileName = "");

public sealed record UploadedPhoto(string FileName, string ContentType);

public class PhotoService
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpgSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] WebpRiffSignature = { 0x52, 0x49, 0x46, 0x46 };
    private static readonly byte[] WebpFormatSignature = { 0x57, 0x45, 0x42, 0x50 };

    private readonly IPhotoStorage _storage;

    public PhotoService(IPhotoStorage storage)
    {
        _storage = storage;
    }

    public Task<StoredPhoto?> OpenPhotoAsync(string fileName, string contentType)
    {
        return _storage.OpenReadAsync(PhotoKeys.GetSafeFileName(fileName), contentType);
    }

    public Task DeletePhotoFilesAsync(IEnumerable<string> fileNames) =>
        _storage.DeleteManyAsync(fileNames
            .Select(PhotoKeys.TryGetSafeFileName)
            .OfType<string>()
            .Distinct());

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

        var sourceFileName = PhotoKeys.GetSafeFileName(sourceItem.PhotoFileName);
        var extension = Path.GetExtension(sourceFileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var copied = await _storage.CopyAsync(sourceFileName, fileName);
        if (!copied)
            return;

        targetItem.PhotoFileName = fileName;
        targetItem.PhotoContentType = sourceItem.PhotoContentType;
    }

    public async Task<UploadedPhoto?> UploadPhotoAsync(Dtos.ItemUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PhotoDataUrl))
            return null;

        var photo = DecodePhotoDataUrl(request.PhotoDataUrl!);
        var fileName = $"{Guid.NewGuid():N}{photo.Extension}";
        await _storage.SaveAsync(fileName, photo.Bytes, photo.ContentType);
        return new UploadedPhoto(fileName, photo.ContentType);
    }

    public PhotoChangeResult ApplyPhotoChange(Item item, Dtos.ItemUpsertRequest request, UploadedPhoto? uploadedPhoto)
    {
        var oldPhotoFileName = item.PhotoFileName;

        if (request.RemovePhoto || uploadedPhoto is not null)
        {
            item.PhotoFileName = string.Empty;
            item.PhotoContentType = string.Empty;
        }

        if (uploadedPhoto is null)
        {
            return request.RemovePhoto
                ? new PhotoChangeResult(ReplacedFileName: oldPhotoFileName)
                : new PhotoChangeResult();
        }

        item.PhotoFileName = uploadedPhoto.FileName;
        item.PhotoContentType = uploadedPhoto.ContentType;
        return new PhotoChangeResult(uploadedPhoto.FileName, oldPhotoFileName);
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
