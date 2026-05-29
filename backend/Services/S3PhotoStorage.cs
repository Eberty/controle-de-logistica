using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace AssetManagement.Services;

public class S3PhotoStorage : IPhotoStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3PhotoStorageOptions _options;

    public S3PhotoStorage(IOptions<PhotoStorageOptions> options)
    {
        _options = options.Value.S3;
        if (string.IsNullOrWhiteSpace(_options.Bucket))
            throw new InvalidOperationException("Configure Storage:S3:Bucket para usar S3.");

        _client = CreateClient(_options);
    }

    public async Task SaveAsync(string key, byte[] bytes, string contentType)
    {
        await using var stream = new MemoryStream(bytes);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = GetObjectKey(key),
            InputStream = stream,
            ContentType = contentType
        });
    }

    public async Task<StoredPhoto?> OpenReadAsync(string key, string contentType)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, GetObjectKey(key));
            var resolvedContentType = string.IsNullOrWhiteSpace(contentType)
                ? response.Headers.ContentType
                : contentType;
            return new StoredPhoto(response.ResponseStream, resolvedContentType);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task<bool> CopyAsync(string sourceKey, string targetKey)
    {
        try
        {
            await _client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _options.Bucket,
                SourceKey = GetObjectKey(sourceKey),
                DestinationBucket = _options.Bucket,
                DestinationKey = GetObjectKey(targetKey)
            });
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public async Task DeleteManyAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await _client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _options.Bucket,
                    Key = GetObjectKey(key)
                });
            }
            catch (Exception)
            {
                // Cleanup failures should not undo an already committed change.
            }
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static IAmazonS3 CreateClient(S3PhotoStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            config.ServiceURL = options.ServiceUrl;
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);

        if (string.IsNullOrWhiteSpace(options.AccessKey))
            return new AmazonS3Client(config);

        if (string.IsNullOrWhiteSpace(options.SecretKey))
            throw new InvalidOperationException("Configure Storage:S3:SecretKey para usar Storage:S3:AccessKey.");

        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        return new AmazonS3Client(credentials, config);
    }

    private string GetObjectKey(string key)
    {
        var fileName = PhotoKeys.GetSafeFileName(key);
        var prefix = (_options.KeyPrefix ?? string.Empty).Trim().Trim('/');
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.NotFound
        || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);
}
