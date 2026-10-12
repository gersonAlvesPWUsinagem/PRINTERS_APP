using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/administracao/retencoes")]
[Authorize]
public sealed class RetencoesAdministracaoController(AppDbContext db) : ControllerBase
{
    [HttpGet("usuarios/{usuarioId:int}")]
    public async Task<ActionResult<RetencoesUsuarioResponse>> ObterPorUsuario(
        int usuarioId,
        CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        var usuario = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == usuarioId, cancellationToken);
        if (usuario is null) return NotFound("Usuário não encontrado.");

        var identificadores = new[] { usuario.UsernamePc, usuario.Login }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => NormalizarUsername(x!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var documentos = identificadores.Count == 0
            ? []
            : await db.DocumentosRetidos.AsNoTracking()
                .Where(x => identificadores.Contains(x.Username.ToLower()))
                .OrderByDescending(x => x.DataEnvio)
                .Select(x => new DocumentoRetidoAdministracaoResponse(
                    x.Id,
                    x.Username,
                    x.NomeArquivo,
                    x.NumeroPaginas,
                    x.DataEnvio,
                    x.Status,
                    x.ImpressoraDestino,
                    x.DadosArquivo.Length))
                .ToListAsync(cancellationToken);

        return Ok(new RetencoesUsuarioResponse(
            usuario.UserId,
            usuario.Name,
            usuario.Login,
            usuario.Registration,
            usuario.Level,
            usuario.UsernamePc,
            usuario.CanPrint,
            !string.IsNullOrWhiteSpace(usuario.Login) &&
                !string.IsNullOrWhiteSpace(usuario.Password) &&
                !string.IsNullOrWhiteSpace(usuario.Registration),
            documentos.Any(x => x.Status == StatusDocumento.Pendente),
            documentos.Count,
            documentos.Sum(x => x.NumeroPaginas),
            documentos.Count(x => x.Status == StatusDocumento.Pendente),
            documentos.Where(x => x.Status == StatusDocumento.Pendente).Sum(x => x.NumeroPaginas),
            documentos));
    }

    private bool EhAdministrador()
    {
        var nivel = User.FindFirstValue("Level");
        var servico = User.FindFirstValue("Service");
        return nivel is "6" or "2" ||
            string.Equals(nivel, "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nivel, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(servico, "PRINTERS_APP", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizarUsername(string valor)
    {
        var username = valor.Trim();
        var separador = username.LastIndexOf('\\');
        if (separador >= 0 && separador < username.Length - 1)
            username = username[(separador + 1)..];
        return username.ToLowerInvariant();
    }
}

public sealed record RetencoesUsuarioResponse(
    int UsuarioId,
    string? Nome,
    string? Login,
    string Matricula,
    string? Nivel,
    string? UsernamePc,
    bool PodeImprimir,
    bool CadastroCompleto,
    bool PossuiDocumentosRetidos,
    int QuantidadeDocumentos,
    int TotalPaginas,
    int QuantidadePendentes,
    int TotalPaginasPendentes,
    IReadOnlyCollection<DocumentoRetidoAdministracaoResponse> Documentos);

public sealed record DocumentoRetidoAdministracaoResponse(
    int Id,
    string Username,
    string NomeArquivo,
    int NumeroPaginas,
    DateTime DataEnvio,
    string Status,
    string? ImpressoraDestino,
    int TamanhoBytes);
