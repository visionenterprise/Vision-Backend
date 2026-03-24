using Google.Apis.Auth.OAuth2;
using Google.Cloud.Iam.Credentials.V1;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using vision_backend.Application.Interfaces;
using vision_backend.Application.Options;

namespace vision_backend.Infrastructure.Services;

public class GcsStorageService : IStorageService
{
    private readonly StorageClient _storageClient;
    private readonly GoogleCredential _credential;
    private readonly UrlSigner? _urlSigner;
    private readonly IMemoryCache _cache;
    private readonly string? _signerServiceAccountEmail;
    private readonly string _webRootPath;
    private readonly string _localUploadRoot;
    private readonly string _publicBaseUrl;
    private readonly string _bucket;
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(1);
    private static readonly TimeSpan SignedUrlCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public GcsStorageService(
        StorageClient storageClient,
        GoogleCredential credential,
        IMemoryCache cache,
        IWebHostEnvironment environment,
        IOptions<GcpStorageOptions> options)
    {
        _storageClient = storageClient;
        _credential = credential;
        _cache = cache;
        _webRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        _localUploadRoot = Path.Combine(_webRootPath, "uploads");
        _publicBaseUrl = string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)
            ? "http://localhost:5243"
            : options.Value.PublicBaseUrl.TrimEnd('/');
        _bucket = options.Value.BucketName;
        _signerServiceAccountEmail = string.IsNullOrWhiteSpace(options.Value.SignerServiceAccountEmail)
            ? null
            : options.Value.SignerServiceAccountEmail;

        if (string.IsNullOrWhiteSpace(_bucket))
        {
            throw new InvalidOperationException("GcpStorage:BucketName is not configured.");
        }

        try
        {
            _urlSigner = UrlSigner.FromCredential(_credential);
        }
        catch
        {
            _urlSigner = CreateIamBackedUrlSigner();
        }
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        try
        {
            await _storageClient.UploadObjectAsync(
                bucket: _bucket,
                objectName: key,
                contentType: contentType,
                source: stream);

            return key;
        }
        catch
        {
            var normalized = NormalizeKey(key);
            var localPath = GetLocalPath(normalized);
            var localDirectory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(localDirectory))
            {
                Directory.CreateDirectory(localDirectory);
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            await using var fileStream = File.Create(localPath);
            await stream.CopyToAsync(fileStream);

            return BuildLocalKey(normalized);
        }
    }

    public async Task DeleteAsync(string key)
    {
        if (IsLocalKey(key))
        {
            var localPath = GetLocalPath(NormalizeKey(key));
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
            return;
        }

        try
        {
            await _storageClient.DeleteObjectAsync(_bucket, key);
        }
        catch
        {
            var localPath = GetLocalPath(NormalizeKey(key));
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    public string GetPresignedUrl(string key, TimeSpan? expiry = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(key, UriKind.Absolute, out var absoluteUrl) &&
            (absoluteUrl.Scheme == Uri.UriSchemeHttp || absoluteUrl.Scheme == Uri.UriSchemeHttps))
        {
            return key;
        }

        if (IsLocalKey(key))
        {
            var normalized = NormalizeKey(key);
            return BuildAbsoluteUrl($"/uploads/{EscapeObjectKey(normalized)}");
        }

        var effectiveExpiry = expiry ?? DefaultExpiry;
        var cacheKey = $"gcs-signed-url:{_bucket}:{key}:{(int)effectiveExpiry.TotalSeconds}";

        if (_cache.TryGetValue<string>(cacheKey, out var cachedUrl) && !string.IsNullOrWhiteSpace(cachedUrl))
        {
            return cachedUrl;
        }

        if (_urlSigner is not null)
        {
            try
            {
                var signedUrl = _urlSigner.Sign(
                    _bucket,
                    key,
                    effectiveExpiry,
                    HttpMethod.Get);

                _cache.Set(cacheKey, signedUrl, SignedUrlCacheDuration);
                return signedUrl;
            }
            catch
            {
                return BuildAbsoluteUrl($"/api/files/{EscapeObjectKey(key)}");
            }
        }

        return BuildAbsoluteUrl($"/api/files/{EscapeObjectKey(key)}");
    }

    public async Task<(Stream Stream, string ContentType)> OpenReadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("File key is empty.");

        if (IsLocalKey(key))
        {
            var normalized = NormalizeKey(key);
            var localPath = GetLocalPath(normalized);
            if (!File.Exists(localPath))
                throw new InvalidOperationException("Requested file was not found.");

            var stream = File.OpenRead(localPath);
            var contentType = ResolveContentType(normalized);
            return (stream, contentType);
        }

        var objectName = NormalizeKey(key);
        var obj = await _storageClient.GetObjectAsync(_bucket, objectName);
        var memory = new MemoryStream();
        await _storageClient.DownloadObjectAsync(_bucket, objectName, memory);
        memory.Position = 0;
        return (memory, string.IsNullOrWhiteSpace(obj.ContentType) ? ResolveContentType(objectName) : obj.ContentType);
    }

    private UrlSigner? CreateIamBackedUrlSigner()
    {
        if (string.IsNullOrWhiteSpace(_signerServiceAccountEmail))
        {
            return null;
        }

        try
        {
            var iamClient = new IAMCredentialsClientBuilder
            {
                Credential = _credential,
            }.Build();

            return UrlSigner.FromBlobSigner(new IamBlobSigner(iamClient, _signerServiceAccountEmail));
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeObjectKey(string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private string GetLocalPath(string normalizedKey)
        => Path.Combine(_localUploadRoot, normalizedKey.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsLocalKey(string key)
        => key.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    private static string BuildLocalKey(string normalizedKey)
        => $"local:{normalizedKey}";

    private static string NormalizeKey(string key)
    {
        var normalized = IsLocalKey(key) ? key[6..] : key;
        if (normalized.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return normalized[9..];
        if (normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            return normalized[8..];
        return normalized;
    }

    private string BuildAbsoluteUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _publicBaseUrl;

        return $"{_publicBaseUrl}/{path.TrimStart('/')}";
    }

    private static string ResolveContentType(string key)
    {
        if (ContentTypeProvider.TryGetContentType(key, out var contentType) && !string.IsNullOrWhiteSpace(contentType))
            return contentType;

        return "application/octet-stream";
    }

    private sealed class IamBlobSigner : UrlSigner.IBlobSigner
    {
        private readonly IAMCredentialsClient _client;
        private readonly string _serviceAccountResource;
        public string Id { get; }
        public string Algorithm => "GOOG4-RSA-SHA256";

        public IamBlobSigner(IAMCredentialsClient client, string serviceAccountEmail)
        {
            _client = client;
            _serviceAccountResource = $"projects/-/serviceAccounts/{serviceAccountEmail}";
            Id = serviceAccountEmail;
        }

        public string CreateSignature(byte[] data, UrlSigner.BlobSignerParameters parameters)
            => CreateSignatureAsync(data, parameters, CancellationToken.None).GetAwaiter().GetResult();

        public async Task<string> CreateSignatureAsync(byte[] data, UrlSigner.BlobSignerParameters parameters, CancellationToken cancellationToken)
        {
            var response = await _client.SignBlobAsync(new SignBlobRequest
            {
                Name = _serviceAccountResource,
                Payload = Google.Protobuf.ByteString.CopyFrom(data),
            }, cancellationToken: cancellationToken);

            return response.SignedBlob.ToBase64();
        }
    }
}