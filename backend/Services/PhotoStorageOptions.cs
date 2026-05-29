namespace AssetManagement.Services;

public class PhotoStorageOptions
{
    public string Provider { get; set; } = "Local";
    public string LocalPath { get; set; } = "Data/images";
    public S3PhotoStorageOptions S3 { get; set; } = new();
}

public class S3PhotoStorageOptions
{
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = "items";
    public bool ForcePathStyle { get; set; }
}
