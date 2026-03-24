namespace vision_backend.Application.Options;

public class GcpStorageOptions
{
    public string ProjectId { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string SignerServiceAccountEmail { get; set; } = string.Empty;
    public string ImpersonateServiceAccountEmail { get; set; } = string.Empty;
    public string ServiceAccountKeyPath { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;
}