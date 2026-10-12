using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public sealed class LoginRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    public string? Login { get; set; }
    [Required, DataType(DataType.Password)]
    public string? Password { get; set; }
    public string? UserName { get; set; }
    public string? Ipv4 { get; set; }
}

