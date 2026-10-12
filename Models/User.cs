using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PRINTERS_APP.Models;

[Table("TB_USER")]
public sealed class User
{
    [Key]
    [Column("USERID")]
    public int UserId { get; set; }
    [Column("USERNOME")]
    public string? Name { get; set; }
    [Column("USERLOGIN")]
    public string? Login { get; set; }
    [Column("USERNIVEL")]
    public string? Level { get; set; }
    [Column("USERPASS")]
    public string? Password { get; set; }
    [Column("REGISTRO")]
    public string Registration { get; set; } = string.Empty;
    [Column("USERNAMEPC")]
    public string? UsernamePc { get; set; }
    [Column("PODEIMPRIMIR")]
    public bool CanPrint { get; set; }
}
