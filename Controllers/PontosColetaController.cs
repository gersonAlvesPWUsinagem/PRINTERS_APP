using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/pontos-coleta")]
[Authorize]
public sealed class PontosColetaController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PontoColetaResponse>>> Listar(
        CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        var pontos = await ConsultaCompleta().OrderBy(x => x.Nome).ToListAsync(cancellationToken);
        return Ok(pontos.Select(ToResponse));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PontoColetaResponse>> Obter(int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        var ponto = await ConsultaCompleta().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return ponto is null ? NotFound("Ponto de coleta não encontrado.") : Ok(ToResponse(ponto));
    }

    [HttpGet("por-ip")]
    public async Task<ActionResult<PontoColetaResponse>> ObterPorIp(
        [FromQuery, Required] string ipAddress,
        CancellationToken cancellationToken)
    {
        if (!TentarNormalizarIp(ipAddress, out var ipNormalizado))
            return BadRequest("Informe um endereço IP válido.");

        var ponto = await ConsultaCompleta()
            .FirstOrDefaultAsync(x => x.Ativo && x.Impressora.Ativa && x.IpMaquina == ipNormalizado, cancellationToken);

        return ponto is null
            ? NotFound("Não existe ponto de coleta ativo com impressora ativa para o IP informado.")
            : Ok(ToResponse(ponto));
    }

    [HttpPost]
    public async Task<ActionResult<PontoColetaResponse>> Criar(
        [FromBody] SalvarPontoColetaRequest request,
        CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        if (!TentarNormalizarIp(request.IpMaquina, out var ipNormalizado))
            return BadRequest("Informe um endereço IP válido para a máquina do ponto de coleta.");

        var erro = await ValidarAsync(request, ipNormalizado, null, cancellationToken);
        if (erro is not null) return erro;

        var ponto = new PontoColeta();
        Aplicar(ponto, request, ipNormalizado);
        db.PontosColeta.Add(ponto);
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(ponto).Reference(x => x.Impressora).LoadAsync(cancellationToken);

        return CreatedAtAction(nameof(Obter), new { id = ponto.Id }, ToResponse(ponto));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PontoColetaResponse>> Atualizar(
        int id,
        [FromBody] SalvarPontoColetaRequest request,
        CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        if (!TentarNormalizarIp(request.IpMaquina, out var ipNormalizado))
            return BadRequest("Informe um endereço IP válido para a máquina do ponto de coleta.");

        var ponto = await db.PontosColeta.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (ponto is null) return NotFound("Ponto de coleta não encontrado.");

        var erro = await ValidarAsync(request, ipNormalizado, id, cancellationToken);
        if (erro is not null) return erro;

        Aplicar(ponto, request, ipNormalizado);
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(ponto).Reference(x => x.Impressora).LoadAsync(cancellationToken);
        return Ok(ToResponse(ponto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        var ponto = await db.PontosColeta.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (ponto is null) return NotFound("Ponto de coleta não encontrado.");

        db.PontosColeta.Remove(ponto);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private IQueryable<PontoColeta> ConsultaCompleta() =>
        db.PontosColeta.AsNoTracking().Include(x => x.Impressora);

    private async Task<ActionResult?> ValidarAsync(
        SalvarPontoColetaRequest request,
        string ipNormalizado,
        int? id,
        CancellationToken cancellationToken)
    {
        var nome = request.Nome.Trim();
        if (!await db.ImpressorasConfig.AnyAsync(x => x.Id == request.ImpressoraId, cancellationToken))
            return NotFound("Impressora não encontrada.");
        if (await db.PontosColeta.AnyAsync(x => x.Id != id && x.Nome == nome, cancellationToken))
            return Conflict("Já existe um ponto de coleta com esse nome.");
        if (await db.PontosColeta.AnyAsync(x => x.Id != id && x.IpMaquina == ipNormalizado, cancellationToken))
            return Conflict("Já existe um ponto de coleta cadastrado para esse IP de máquina.");
        return null;
    }

    private static void Aplicar(PontoColeta ponto, SalvarPontoColetaRequest request, string ipNormalizado)
    {
        ponto.Nome = request.Nome.Trim();
        ponto.IpMaquina = ipNormalizado;
        ponto.ImpressoraId = request.ImpressoraId;
        ponto.Ativo = request.Ativo;
        ponto.TokenValidadeDias = request.TokenValidadeDias;
    }

    private static bool TentarNormalizarIp(string? valor, out string ipNormalizado)
    {
        if (IPAddress.TryParse(valor?.Trim(), out var ip))
        {
            ipNormalizado = ip.MapToIPv6().IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
            return true;
        }

        ipNormalizado = string.Empty;
        return false;
    }

    private bool EhAdministrador()
    {
        var nivel = User.FindFirstValue("Level");
        return nivel == "2" ||
            string.Equals(nivel, "ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nivel, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }

    private static PontoColetaResponse ToResponse(PontoColeta ponto) => new(
        ponto.Id,
        ponto.Nome,
        ponto.IpMaquina,
        ponto.Ativo,
        ponto.TokenValidadeDias,
        new ImpressoraPontoColetaResponse(
            ponto.Impressora.Id,
            ponto.Impressora.NomeImpressora,
            ponto.Impressora.IpFisico,
            ponto.Impressora.Ativa));
}

public sealed record SalvarPontoColetaRequest
{
    [Required, StringLength(150)]
    public required string Nome { get; init; }

    [Required, StringLength(45)]
    public required string IpMaquina { get; init; }

    [Range(1, int.MaxValue)]
    public int ImpressoraId { get; init; }

    public bool Ativo { get; init; } = true;

    [Range(1, 3650)]
    public int TokenValidadeDias { get; init; } = 360;
}

public sealed record PontoColetaResponse(
    int Id,
    string Nome,
    string IpMaquina,
    bool Ativo,
    int TokenValidadeDias,
    ImpressoraPontoColetaResponse Impressora);

public sealed record ImpressoraPontoColetaResponse(
    int Id,
    string Nome,
    string IpFisico,
    bool Ativa);
