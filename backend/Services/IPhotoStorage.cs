namespace AssetManagement.Services;

public sealed record StoredPhoto(Stream Stream, string ContentType);

public interface IPhotoStorage
{
    Task SaveAsync(string key, byte[] bytes, string contentType);
    Task<StoredPhoto?> OpenReadAsync(string key, string contentType);
    Task<bool> CopyAsync(string sourceKey, string targetKey);
    Task DeleteManyAsync(IEnumerable<string> keys);
}
