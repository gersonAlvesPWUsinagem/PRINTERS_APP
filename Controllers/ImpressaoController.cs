using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Printing;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;
using PRINTERS_APP.Repositories;
using PRINTERS_APP.Services;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/impressao")]
public sealed class ImpressaoController(
    IImpressaoRepository repository,
    IPrintSpooler printSpooler,
    AppDbContext db,
    ApiLogService apiLog,
    IConfiguration configuration) : ControllerBase
{
    private int PortaImpressora => configuration.GetValue("Printing:Port", 9100);

    [HttpPost("receber")]
    [AllowAnonymous]
    public async Task<ActionResult<DocumentoRetidoResponse>> Receber(
        [FromBody] ReceberDocumentoRequest request,
        CancellationToken cancellationToken)
    {
        var usernamePc = NormalizarUsernamePc(request.Username);
        var usuario = await db.Users.FirstOrDefaultAsync(
            x => x.UsernamePc == usernamePc || x.Login == usernamePc,
            cancellationToken);

        if (usuario is null)
        {
            var nextUserId = db.Users.Select(x => x.UserId).ToList().DefaultIfEmpty(0).Max() + 1;
            usuario = new User
            {
                UserId = nextUserId,
                Name = usernamePc,
                UsernamePc = usernamePc,
                Login = null,
                Password = null,
                Registration = string.Empty,
                Level = "1"
            };
            db.Users.Add(usuario);
            await db.SaveChangesAsync(cancellationToken);
            await apiLog.RegistrarAsync(
                "AVISO", "USUARIO_INCOMPLETO_CRIADO",
                $"Cadastro incompleto criado para o usuário do computador {usernamePc}.",
                HttpContext.Request.Path, usernamePc, cancellationToken: cancellationToken);
        }
        else if (string.IsNullOrWhiteSpace(usuario.UsernamePc))
        {
            usuario.UsernamePc = usernamePc;
            await db.SaveChangesAsync(cancellationToken);
        }

        var recebimento = await repository.AddDocumentoSeNaoExistirAsync(new DocumentoRetido
        {
            Username = usernamePc,
            NomeArquivo = request.NomeArquivo.Trim(),
            NumeroPaginas = Math.Max(1, request.NumeroPaginas),
            DadosArquivo = request.DadosArquivo,
            DataEnvio = DateTime.Now,
            Status = StatusDocumento.Pendente
        }, cancellationToken);
        var documento = recebimento.Documento;

        if (!recebimento.Criado)
        {
            await apiLog.RegistrarAsync(
                "INFO", "DOCUMENTO_DUPLICADO_IGNORADO",
                $"O reenvio do documento {documento.NomeArquivo} foi ignorado porque ele já existe no SQLite.",
                HttpContext.Request.Path, usernamePc,
                $"DocumentoId={documento.Id}; Arquivo={documento.NomeArquivo}; Paginas={documento.NumeroPaginas}; Bytes={documento.DadosArquivo.Length}; Usuario={usernamePc}",
                cancellationToken);
            return Ok(ToResponse(documento));
        }

        await apiLog.RegistrarAsync(
            "INFO", "DOCUMENTO_RECEBIDO",
            $"Documento {documento.NomeArquivo} recebido com {documento.NumeroPaginas} página(s) e {documento.DadosArquivo.Length} bytes.",
            HttpContext.Request.Path, usernamePc, $"DocumentoId={documento.Id}", cancellationToken);

        return CreatedAtAction(nameof(ObterPendente), new { id = documento.Id }, ToResponse(documento));
    }

    [HttpGet("pendentes")]
    [Authorize(Policy = "Impressao")]
    public async Task<ActionResult<IReadOnlyCollection<DocumentoRetidoResponse>>> Pendentes(CancellationToken cancellationToken)
    {
        var documentos = await repository.ListarPendentesAsync(ObterIdentificadoresUsuario(), cancellationToken);
        return Ok(documentos.Select(ToResponse).ToList());
    }

    [HttpGet("pendentes/{id:int}")]
    [Authorize(Policy = "Impressao")]
    public async Task<ActionResult<DocumentoRetidoResponse>> ObterPendente(int id, CancellationToken cancellationToken)
    {
        var documento = await repository.ObterPendenteDoUsuarioAsync(id, ObterIdentificadoresUsuario(), cancellationToken);
        return documento is null ? NotFound() : Ok(ToResponse(documento));
    }

    [HttpDelete("pendentes/{id:int}")]
    [Authorize(Policy = "Impressao")]
    public async Task<IActionResult> ExcluirPendente(int id, CancellationToken cancellationToken)
    {
        var documento = await repository.ExcluirPendenteAsync(id, ObterIdentificadoresUsuario(), cancellationToken);
        if (documento is null)
            return NotFound("Documento pendente não encontrado.");

        await apiLog.RegistrarAsync(
            "INFO", "DOCUMENTO_EXCLUIDO",
            $"Documento {documento.NomeArquivo} excluído da fila pelo usuário.",
            HttpContext.Request.Path,
            documento.Username,
            $"DocumentoId={documento.Id}; Arquivo={documento.NomeArquivo}; Paginas={documento.NumeroPaginas}; Bytes={documento.DadosArquivo.Length}; UsuarioDocumento={documento.Username}; UsuarioLogin={User.FindFirstValue("Login")}",
            cancellationToken);
        return NoContent();
    }
    [HttpPost("liberar")]
    [Authorize(Policy = "Impressao")]
    public async Task<IActionResult> Liberar(
    [FromBody] LiberarDocumentoRequest request,
    CancellationToken cancellationToken)
    {
        var documento = await repository.ObterPendenteDoUsuarioAsync(request.DocumentoId, ObterIdentificadoresUsuario(), cancellationToken);
        if (documento is null)
        {
            await apiLog.RegistrarAsync(
                "ERRO", "DOCUMENTO_NAO_ENCONTRADO",
                $"Documento {request.DocumentoId} não encontrado para liberação.",
                HttpContext.Request.Path,
                User.FindFirstValue("Login"),
                $"DocumentoId={request.DocumentoId}; UsuarioLogin={User.FindFirstValue("Login")}; Motivo=Documento inexistente, já processado ou não pertencente ao usuário.",
                cancellationToken);
            return NotFound("Documento não encontrado ou não pertence ao usuário autenticado.");
        }

        if (!IPAddress.TryParse(request.IpMaquina.Trim(), out var enderecoMaquina))
        {
            await RegistrarErroValidacaoAsync(
                documento,
                "IP_PONTO_COLETA_INVALIDO",
                $"IP informado: {request.IpMaquina}",
                cancellationToken);
            return BadRequest("Informe um endereço IP válido para a máquina do ponto de coleta.");
        }

        var ipMaquina = enderecoMaquina.MapToIPv6().IsIPv4MappedToIPv6
            ? enderecoMaquina.MapToIPv4().ToString()
            : enderecoMaquina.ToString();

        var pontoColeta = await db.PontosColeta
            .AsNoTracking()
            .Include(x => x.Impressora)
            .FirstOrDefaultAsync(
                x => x.Ativo && x.IpMaquina == ipMaquina && x.Impressora.Ativa,
                cancellationToken);
        if (pontoColeta is null)
        {
            await RegistrarErroValidacaoAsync(
                documento,
                "PONTO_COLETA_NAO_ENCONTRADO",
                $"IP da máquina: {ipMaquina}",
                cancellationToken);
            return BadRequest("O ponto de coleta não está cadastrado, está inativo ou não possui impressora ativa.");
        }

        var impressora = pontoColeta.Impressora;

        try
        {
            await apiLog.RegistrarAsync(
                "INFO", "IMPRESSAO_INICIADA",
                $"Enviando documento {documento.Id} para {impressora.IpFisico}:{PortaImpressora} (ponto {pontoColeta.Nome}, máquina {ipMaquina}).",
                HttpContext.Request.Path, documento.Username,
                $"Arquivo={documento.NomeArquivo}; Bytes={documento.DadosArquivo.Length}; IpMaquina={ipMaquina}; PontoColetaId={pontoColeta.Id}", cancellationToken);

            await printSpooler.EnviarAsync(
                impressora.NomeImpressora,
                impressora.IpFisico,
                PortaImpressora,
                documento.NomeArquivo,
                documento.DadosArquivo,
                cancellationToken);
        }
        catch (PrintSystemException ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: $"Não foi possível enviar o documento para a fila da impressora: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Win32Exception ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: $"O spooler do Windows recusou o documento: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (IOException ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: $"O spooler não recebeu o documento completo: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: $"Não foi possível acessar a impressora configurada: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: "Ocorreu uma falha inesperada ao enviar o documento para impressão.", statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            documento.Status = StatusDocumento.Impresso;
            documento.ImpressoraDestino = impressora.IpFisico;
            await repository.AtualizarDocumentoAsync(documento, cancellationToken);

            await apiLog.RegistrarAsync(
                "INFO", "IMPRESSAO_CONCLUIDA",
                $"Documento {documento.Id} enviado para {impressora.IpFisico}:{PortaImpressora}.",
                HttpContext.Request.Path, documento.Username,
                $"Arquivo={documento.NomeArquivo}; Bytes={documento.DadosArquivo.Length}", cancellationToken);
        }
        catch (Exception ex)
        {
            await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
            return Problem(detail: "O documento foi enviado, mas não foi possível atualizar seu estado na aplicação.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(new { documento.Id, documento.Status, Impressora = impressora.NomeImpressora, Endereco = impressora.IpFisico });
    }

    //[HttpPost("liberar")]
    //[Authorize]
    //public async Task<IActionResult> Liberar(
    //    [FromBody] LiberarDocumentoRequest request,
    //    CancellationToken cancellationToken)
    //{
    //    var documento = await repository.ObterPendenteDoUsuarioAsync(request.DocumentoId, ObterIdentificadoresUsuario(), cancellationToken);
    //    if (documento is null)
    //    {
    //        await apiLog.RegistrarAsync(
    //            "AVISO", "DOCUMENTO_NAO_ENCONTRADO",
    //            $"Documento {request.DocumentoId} não encontrado para liberação.",
    //            HttpContext.Request.Path, User.FindFirstValue("Login"), cancellationToken: cancellationToken);
    //        return NotFound("Documento não encontrado ou não pertence ao usuário autenticado.");
    //    }

    //    var impressora = await repository.ObterImpressoraAtivaAsync(request.ImpressoraDestino.Trim(), cancellationToken);
    //    if (impressora is null)
    //        return BadRequest("A impressora informada pelo ponto de retirada não está cadastrada ou está inativa.");

    //    try
    //    {
    //        await apiLog.RegistrarAsync(
    //            "INFO", "IMPRESSAO_INICIADA",
    //            $"Enviando documento {documento.Id} para {impressora.IpFisico}:{PortaImpressora}.",
    //            HttpContext.Request.Path, documento.Username,
    //            $"Arquivo={documento.NomeArquivo}; Bytes={documento.DadosArquivo.Length}", cancellationToken);
    //        await printSpooler.EnviarAsync(
    //            impressora.NomeImpressora,
    //            impressora.IpFisico,
    //            PortaImpressora,
    //            documento.NomeArquivo,
    //            documento.DadosArquivo,
    //            cancellationToken);
    //    }
    //    catch (PrintSystemException ex)
    //    {
    //        await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
    //        return Problem(detail: $"Não foi possível enviar o documento para a fila da impressora: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    //    }
    //    catch (Win32Exception ex)
    //    {
    //        await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
    //        return Problem(detail: $"O spooler do Windows recusou o documento: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    //    }
    //    catch (IOException ex)
    //    {
    //        await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
    //        return Problem(detail: $"O spooler não recebeu o documento completo: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    //    }
    //    catch (InvalidOperationException ex)
    //    {
    //        await RegistrarFalhaImpressaoAsync(documento, impressora.IpFisico, ex, cancellationToken);
    //        return Problem(detail: $"Não foi possível acessar a impressora configurada: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    //    }

    //    documento.Status = StatusDocumento.Impresso;
    //    documento.ImpressoraDestino = impressora.IpFisico;
    //    await repository.AtualizarDocumentoAsync(documento, cancellationToken);

    //    await apiLog.RegistrarAsync(
    //        "INFO", "IMPRESSAO_CONCLUIDA",
    //        $"Documento {documento.Id} enviado para {impressora.IpFisico}:{PortaImpressora}.",
    //        HttpContext.Request.Path, documento.Username,
    //        $"Arquivo={documento.NomeArquivo}; Bytes={documento.DadosArquivo.Length}", cancellationToken);

    //    return Ok(new { documento.Id, documento.Status, Impressora = impressora.NomeImpressora, Endereco = impressora.IpFisico });
    //}

    [HttpGet("impressoras")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyCollection<ImpressoraResponse>>> Impressoras(CancellationToken cancellationToken)
    {
        var impressoras = await repository.ListarImpressorasAsync(cancellationToken);
        return Ok(impressoras.Select(x => new ImpressoraResponse(x.Id, x.NomeImpressora, x.IpFisico, x.Ativa)).ToList());
    }

    [HttpPost("impressoras")]
    [Authorize]
    public async Task<ActionResult<ImpressoraResponse>> CadastrarImpressora(
        [FromBody] CadastrarImpressoraRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var impressora = await repository.AddImpressoraAsync(new ImpressoraConfig
            {
                NomeImpressora = request.NomeImpressora.Trim(),
                IpFisico = request.IpFisico.Trim(),
                Ativa = request.Ativa
            }, cancellationToken);
            var response = new ImpressoraResponse(impressora.Id, impressora.NomeImpressora, impressora.IpFisico, impressora.Ativa);
            return Created($"/api/impressao/impressoras/{impressora.Id}", response);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return Conflict("Já existe uma impressora com esse nome ou IP físico.");
        }
    }

    private List<string> ObterIdentificadoresUsuario()
    {
        var usernamePc = User.FindFirstValue("UsernamePc")?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(usernamePc) ? [] : [usernamePc];
    }

    private Task RegistrarFalhaImpressaoAsync(DocumentoRetido documento, string enderecoImpressora, Exception exception, CancellationToken cancellationToken) =>
        apiLog.RegistrarAsync(
            "ERRO", "IMPRESSAO_FALHOU",
            $"Falha ao enviar documento {documento.Id} para {enderecoImpressora}:{PortaImpressora}: {exception.Message}",
            HttpContext.Request.Path,
            documento.Username,
            $"DocumentoId={documento.Id}; Arquivo={documento.NomeArquivo}; Paginas={documento.NumeroPaginas}; Bytes={documento.DadosArquivo.Length}; UsuarioDocumento={documento.Username}; UsuarioLogin={User.FindFirstValue("Login")}; Impressora={enderecoImpressora}; Motivo={exception.Message}; Excecao={exception}",
            cancellationToken);

    private Task RegistrarErroValidacaoAsync(
        DocumentoRetido documento,
        string evento,
        string motivo,
        CancellationToken cancellationToken) =>
        apiLog.RegistrarAsync(
            "ERRO",
            evento,
            $"Não foi possível liberar o documento {documento.Id}: {motivo}",
            HttpContext.Request.Path,
            documento.Username,
            $"DocumentoId={documento.Id}; Arquivo={documento.NomeArquivo}; Paginas={documento.NumeroPaginas}; Bytes={documento.DadosArquivo.Length}; UsuarioDocumento={documento.Username}; UsuarioLogin={User.FindFirstValue("Login")}; Motivo={motivo}",
            cancellationToken);

    private static string NormalizarUsernamePc(string valor)
    {
        var username = valor.Trim();
        var separador = username.LastIndexOf('\\');
        if (separador >= 0 && separador < username.Length - 1)
            username = username[(separador + 1)..];
        return username.ToLowerInvariant();
    }

    private static DocumentoRetidoResponse ToResponse(DocumentoRetido documento) =>
        new(documento.Id, documento.Username, documento.NomeArquivo, documento.NumeroPaginas, documento.DataEnvio, documento.Status, documento.ImpressoraDestino);
}

public sealed record ReceberDocumentoRequest
{
    [Required, StringLength(100)]
    public required string Username { get; init; }

    [Required, StringLength(255)]
    public required string NomeArquivo { get; init; }

    [Range(1, int.MaxValue)]
    public int NumeroPaginas { get; init; } = 1;

    [Required, MinLength(1)]
    public required byte[] DadosArquivo { get; init; }
}

public sealed record LiberarDocumentoRequest
{
    [Range(1, int.MaxValue)]
    public int DocumentoId { get; init; }

    [Required, StringLength(45)]
    public required string IpMaquina { get; init; }
}

public sealed record CadastrarImpressoraRequest
{
    [Required, StringLength(150)]
    public required string NomeImpressora { get; init; }

    [Required, StringLength(255)]
    public required string IpFisico { get; init; }

    public bool Ativa { get; init; } = true;
}

public sealed record DocumentoRetidoResponse(
    int Id,
    string Username,
    string NomeArquivo,
    int NumeroPaginas,
    DateTime DataEnvio,
    string Status,
    string? ImpressoraDestino);

public sealed record ImpressoraResponse(int Id, string NomeImpressora, string IpFisico, bool Ativa);
