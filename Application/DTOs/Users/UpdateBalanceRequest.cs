using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Users;

public class UpdateBalanceRequest
{
    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    /// <summary>
    /// "set" (default) — replaces the current balance with Amount.
    /// "add" — adds Amount on top of the existing balance.
    /// </summary>
    public string Mode { get; set; } = "set";
}
