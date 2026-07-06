using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace AssetManagement.Services;

public class LocalPhotoStorage : IPhotoStorage
{
    private readonly IWebHostEnvironment _environment;
    private readonly PhotoStorageOptions _options;

    public LocalPhotoStorage(IWebHostEnvironment environment, IOptions<PhotoStorageOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task SaveAsync(string key, byte[] bytes, string contentType)
    {
        var path = GetPhotoPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
    }

    public Task<StoredPhoto?> OpenReadAsync(string key, string contentType)
    {
        var path = GetPhotoPath(key);
        if (!File.Exists(path))
            return Task.FromResult<StoredPhoto?>(null);

        var resolvedContentType = string.IsNullOrWhiteSpace(contentType)
            ? ResolveContentTypeFromExtension(path)
            : contentType;
        return Task.FromResult<StoredPhoto?>(new StoredPhoto(File.OpenRead(path), resolvedContentType));
    }

    private static string ResolveContentTypeFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

    public async Task<bool> CopyAsync(string sourceKey, string targetKey)
    {
        var sourcePath = GetPhotoPath(sourceKey);
        if (!File.Exists(sourcePath))
            return false;

        var targetPath = GetPhotoPath(targetKey);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var targetStream = File.Create(targetPath);
        await sourceStream.CopyToAsync(targetStream);
        return true;
    }

    public Task DeleteManyAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                var path = GetPhotoPath(key);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // Cleanup failures should not undo an already committed change.
            }
        }

        return Task.CompletedTask;
    }

    private string GetPhotoDirectory()
    {
        if (Path.IsPathRooted(_options.LocalPath))
            return _options.LocalPath;

        return Path.Combine(_environment.ContentRootPath, _options.LocalPath);
    }

    private string GetPhotoPath(string key)
    {
        return Path.Combine(GetPhotoDirectory(), PhotoKeys.GetSafeFileName(key));
    }
}
