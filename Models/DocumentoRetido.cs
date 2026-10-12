using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public sealed class DocumentoRetido
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string NomeArquivo { get; set; } = string.Empty;

    public int NumeroPaginas { get; set; } = 1;

    [Required]
    public byte[] DadosArquivo { get; set; } = Array.Empty<byte>();

    public DateTime DataEnvio { get; set; } = DateTime.Now;

    [Required, StringLength(20)]
    public string Status { get; set; } = StatusDocumento.Pendente;

    [StringLength(255)]
    public string? ImpressoraDestino { get; set; }
}

public static class StatusDocumento
{
    public const string Pendente = "PENDENTE";
    public const string Impresso = "IMPRESSO";
    public const string Cancelado = "CANCELADO";
}
