using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public sealed class ImpressoraConfig
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string NomeImpressora { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string IpFisico { get; set; } = string.Empty;

    public bool Ativa { get; set; } = true;
}