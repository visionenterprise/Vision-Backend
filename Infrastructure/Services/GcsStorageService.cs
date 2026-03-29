using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using vision_backend.Application.Interfaces;
using vision_backend.Application.Options;

namespace vision_backend.Infrastructure.Services;

public class GcsStorageService : IStorageService
{
    private readonly StorageClient _storageClient;
    private readonly string _webRootPath;
    private readonly string _localUploadRoot;
    private readonly string _publicBaseUrl;
    private readonly string _bucket;
    private readonly bool _enableLocalStorage;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public GcsStorageService(
        StorageClient storageClient,
        IWebHostEnvironment environment,
        IOptions<GcpStorageOptions> options)
    {
        _storageClient = storageClient;
        _webRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        _localUploadRoot = Path.Combine(_webRootPath, "uploads");
        _publicBaseUrl = string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)
            ? "http://localhost:5243"
            : options.Value.PublicBaseUrl.TrimEnd('/');
        _bucket = options.Value.BucketName;
        _enableLocalStorage = options.Value.EnableLocalStorage;

        if (string.IsNullOrWhiteSpace(_bucket))
        {
            throw new InvalidOperationException("GcpStorage:BucketName is not configured.");
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
        catch (Exception ex)
        {
            if (!_enableLocalStorage)
            {
                throw new InvalidOperationException($"GCS upload failed for key '{key}' and local storage is disabled.", ex);
            }

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
            if (!_enableLocalStorage)
                return;
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
        catch (Exception ex)
        {
            if (_enableLocalStorage)
            {
                var localPath = GetLocalPath(NormalizeKey(key));
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
            }
            else
            {
                Console.WriteLine($"[Storage] GCS delete failed for key '{key}': {ex.Message}");
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

        // Return a permanent, publicly-accessible proxy URL.
        // The backend streams the file from GCS via /api/files/{key},
        // so the URL works for anyone — no GCS auth required from the viewer.
        return BuildAbsoluteUrl($"api/files/{EscapeObjectKey(key)}");
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
}