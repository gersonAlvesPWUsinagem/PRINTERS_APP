using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public sealed class RevokedToken
{
    [Key]
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RevokedAtUtc { get; set; }
}
