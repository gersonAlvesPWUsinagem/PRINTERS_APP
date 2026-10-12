using System.ComponentModel.DataAnnotations;

namespace PRINTERS_APP.Models;

public enum Nivel
{
    [Display(Name = "PADRÃO / SEM NÍVEL")]
    Default = 0,
    [Display(Name = "LEITURA")]
    Ler = 1,
    [Display(Name = "GRAVAÇÃO")]
    Gravar = 2,
    [Display(Name = "ALTERAÇÃO")]
    Alterar = 3,
    [Display(Name = "EXCLUSÃO")]
    Excluir = 4,
    [Display(Name = "SUPERVISÃO")]
    Supervisao = 5,
    [Display(Name = "ADMINISTRADOR")]
    Admin = 6
}
