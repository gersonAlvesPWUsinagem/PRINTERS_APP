namespace PRINTERS_APP.Models;

public sealed class UserPermission
{
    public decimal RegistrationId { get; set; }
    public int UserId { get; set; }
    public decimal PermissionId { get; set; }
    public int Level { get; set; }
    public decimal Code { get; set; }
    public string? PermissionName { get; set; }
    public string? Module { get; set; }
}
