namespace PRINTERS_APP.Models;


public class LoginAppRequest
{
    public string Service { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Ipv4 { get; set; } = string.Empty;
    public int TargetOSType { get; set; }
    public string? CodeSession { get; }

    public LoginAppRequest()
    {
        // Gera um identificador único para a sessão assim que o objeto é instanciado
        CodeSession = Guid.NewGuid().ToString();
    }
}
