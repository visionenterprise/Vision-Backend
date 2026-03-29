using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.Interfaces;

namespace vision_backend.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IStorageService _storage;

    public FilesController(IStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet("{**key}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetFile(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return NotFound();

        try
        {
            var decoded = Uri.UnescapeDataString(key);
            var (stream, contentType) = await _storage.OpenReadAsync(decoded);
            return File(stream, contentType);
        }
        catch
        {
            return NotFound();
        }
    }
}
