namespace vision_backend.Application.Interfaces;

/// <summary>
/// Abstraction for blob/object storage.
/// The DB column stores only the object key (path).
/// Call <see cref="GetPresignedUrl"/> to produce an accessible URL at response-time.
/// </summary>
public interface IStorageService
{
    /// <summary>Uploads a stream to the given object key. Returns the key.</summary>
    Task<string> UploadAsync(Stream stream, string key, string contentType);

    /// <summary>Permanently deletes the object at the given key.</summary>
    Task DeleteAsync(string key);

    /// <summary>
    /// Generates an HTTPS pre-signed GET URL for a private object.
    /// Default expiry is 1 hour.
    /// </summary>
    string GetPresignedUrl(string key, TimeSpan? expiry = null);

    /// <summary>
    /// Opens the object for reading and returns stream + content-type.
    /// </summary>
    Task<(Stream Stream, string ContentType)> OpenReadAsync(string key);
}
