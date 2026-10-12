namespace PRINTERS_APP.Models;

public sealed class ApiLog
{
    public long Id { get; set; }
    public DateTime DataHora { get; set; } = DateTime.Now;
    public string Nivel { get; set; } = "INFO";
    public string Evento { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? Usuario { get; set; }
    public string? Detalhes { get; set; }
}
