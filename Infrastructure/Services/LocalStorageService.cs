using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using vision_backend.Application.Interfaces;
using vision_backend.Application.Options;

namespace vision_backend.Infrastructure.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _webRootPath;
    private readonly string _localUploadRoot;
    private readonly string _publicBaseUrl;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public LocalStorageService(IWebHostEnvironment environment, IOptions<GcpStorageOptions> options)
    {
        _webRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        _localUploadRoot = Path.Combine(_webRootPath, "uploads");
        _publicBaseUrl = string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)
            ? "http://localhost:5243"
            : options.Value.PublicBaseUrl.TrimEnd('/');
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
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

    public Task DeleteAsync(string key)
    {
        var normalized = NormalizeKey(key);
        var localPath = GetLocalPath(normalized);
        if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }

        return Task.CompletedTask;
    }

    public string GetPresignedUrl(string key, TimeSpan? expiry = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (Uri.TryCreate(key, UriKind.Absolute, out var absoluteUrl) &&
            (absoluteUrl.Scheme == Uri.UriSchemeHttp || absoluteUrl.Scheme == Uri.UriSchemeHttps))
        {
            return key;
        }

        var normalized = NormalizeKey(key);
        return BuildAbsoluteUrl($"/uploads/{EscapeObjectKey(normalized)}");
    }

    public Task<(Stream Stream, string ContentType)> OpenReadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("File key is empty.");

        var normalized = NormalizeKey(key);
        var localPath = GetLocalPath(normalized);
        if (!File.Exists(localPath))
            throw new InvalidOperationException("Requested file was not found.");

        var stream = File.OpenRead(localPath);
        var contentType = ResolveContentType(normalized);
        return Task.FromResult((Stream: (Stream)stream, ContentType: contentType));
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
        => $"{_publicBaseUrl}/{path.TrimStart('/')}";

    private static string ResolveContentType(string key)
    {
        if (ContentTypeProvider.TryGetContentType(key, out var contentType) && !string.IsNullOrWhiteSpace(contentType))
            return contentType;

        return "application/octet-stream";
    }

    private static string EscapeObjectKey(string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }
}