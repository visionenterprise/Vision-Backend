using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.DTOs.Vouchers;
using vision_backend.Application.DTOs.Common;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/vouchers")]
[Authorize]
public class VouchersController : ControllerBase
{
    private readonly IVoucherService _voucherService;

    public VouchersController(IVoucherService voucherService)
    {
        _voucherService = voucherService;
    }

    [HttpPost]
    public async Task<ActionResult<VoucherResponse>> CreateVoucher([FromBody] CreateVoucherRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        try
        {
            var response = await _voucherService.CreateVoucherAsync(userId, request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VoucherResponse>> GetById(Guid id)
    {
        try
        {
            var response = await _voucherService.GetVoucherAsync(id);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("my")]
    public async Task<ActionResult<List<VoucherResponse>>> GetMyVouchers()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var vouchers = await _voucherService.GetUserVouchersAsync(userId);
        return Ok(vouchers);
    }

    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<VoucherResponse>>> SearchVouchers([FromQuery] VoucherSearchRequest request)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        var result = await _voucherService.GetVouchersPagedAsync(request, userId, role);
        return Ok(result);
    }

    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<VoucherResponse>> ApproveVoucher(Guid id)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        try
        {
            var response = await _voucherService.ApproveVoucherAsync(id, userId, role);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<VoucherResponse>> RejectVoucher(Guid id, [FromBody] RejectVoucherRequest request)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        try
        {
            var response = await _voucherService.RejectVoucherAsync(id, userId, role, request.Reason);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<VoucherResponse>> ReopenVoucher(Guid id)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        try
        {
            var response = await _voucherService.ReopenVoucherAsync(id, userId, role);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPost("batch-approve")]
    public async Task<ActionResult<BatchApproveResponse>> BatchApproveVouchers([FromBody] BatchApproveRequest request)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        if (request.VoucherIds == null || request.VoucherIds.Count == 0)
            return BadRequest("No voucher IDs provided.");

        var results = new List<VoucherResponse>();
        var errors = new List<string>();

        foreach (var voucherId in request.VoucherIds)
        {
            try
            {
                var response = await _voucherService.ApproveVoucherAsync(voucherId, userId, role);
                results.Add(response);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{voucherId}: {ex.Message}");
            }
        }

        return Ok(new BatchApproveResponse { Approved = results, Errors = errors });
    }

    [Authorize]
    [HttpGet("export")]
    public async Task<IActionResult> ExportVouchers([FromQuery] VoucherExportRequest request)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        try
        {
            var (content, fileName) = await _voucherService.ExportVouchersAsync(request, userId, role);
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("export-pdf")]
    public async Task<IActionResult> ExportVouchersPdf([FromQuery] VoucherExportRequest request)
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        try
        {
            var (content, fileName) = await _voucherService.ExportVouchersPdfAsync(request, userId, role);
            return File(content, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/receipts")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 100L * 1024 * 1024)]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<ActionResult<VoucherResponse>> UploadReceipts(Guid id, [FromForm] List<IFormFile> files)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (files == null || files.Count == 0)
            return BadRequest("No files uploaded.");

        try
        {
            var fileData = new List<(Stream Stream, string FileName, string ContentType)>();
            foreach (var file in files)
            {
                fileData.Add((file.OpenReadStream(), file.FileName, file.ContentType));
            }

            var response = await _voucherService.UploadReceiptsAsync(id, userId, fileData);

            // Dispose streams after upload
            foreach (var (stream, _, _) in fileData)
            {
                await stream.DisposeAsync();
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            // Log the actual error for debugging
            Console.WriteLine($"[Receipt Upload Error] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[Stack Trace] {ex.StackTrace}");
            return StatusCode(500, new { error = "Receipt upload failed", details = ex.Message });
        }
    }

    private bool TryGetRole(out UserRole role)
    {
        role = UserRole.GeneralUser;
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        return !string.IsNullOrWhiteSpace(roleClaim) && Enum.TryParse(roleClaim, out role);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out userId);
    }
}
