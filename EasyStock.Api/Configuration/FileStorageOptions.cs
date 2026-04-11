namespace EasyStock.Api.Configuration;

public sealed class FileStorageOptions
{
    public string Provider { get; set; } = "Local";
    public string LocalRootPath { get; set; } = "uploaded-files";
    public string PublicBaseUrl { get; set; } = "/files";
    public S3StorageOptions S3 { get; set; } = new();
    public AzureFileShareStorageOptions AzureFileShare { get; set; } = new();
}

public sealed class AzureFileShareStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ShareName { get; set; } = "product-images";
}

public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public string? PublicBaseUrl { get; set; }
}
