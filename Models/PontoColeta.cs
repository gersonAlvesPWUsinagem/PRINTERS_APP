using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public sealed class PontoColeta
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Nome { get; set; } = string.Empty;

    [Required, StringLength(45)]
    public string IpMaquina { get; set; } = string.Empty;

    public int ImpressoraId { get; set; }

    public ImpressoraConfig Impressora { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public int TokenValidadeDias { get; set; } = 360;
}
