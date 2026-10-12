using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Services;

public sealed class ApiLogService(AppDbContext db)
{
    public async Task RegistrarAsync(
        string nivel,
        string evento,
        string mensagem,
        string? endpoint = null,
        string? usuario = null,
        string? detalhes = null,
        CancellationToken cancellationToken = default)
    {
        db.ApiLogs.Add(new ApiLog
        {
            DataHora = DateTime.Now,
            Nivel = Limitar(nivel, 20),
            Evento = Limitar(evento, 100),
            Mensagem = Limitar(mensagem, 2000),
            Endpoint = LimitarOuNulo(endpoint, 500),
            Usuario = LimitarOuNulo(usuario, 100),
            Detalhes = LimitarOuNulo(detalhes, 8000)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Limitar(string valor, int tamanho) =>
        valor.Length <= tamanho ? valor : valor[..tamanho];

    private static string? LimitarOuNulo(string? valor, int tamanho) =>
        string.IsNullOrWhiteSpace(valor) ? null : Limitar(valor, tamanho);
}
