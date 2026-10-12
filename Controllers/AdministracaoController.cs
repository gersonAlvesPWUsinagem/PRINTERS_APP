using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;
using PRINTERS_APP.Security;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/administracao")]
[Authorize]
public sealed class AdministracaoController(AppDbContext db) : ControllerBase
{
    [HttpGet("usuarios")]
    public async Task<ActionResult<IReadOnlyCollection<UsuarioAdminResponse>>> Usuarios(CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var usuarios = await db.Users.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.UsernamePc)
            .ToListAsync(cancellationToken);
        return Ok(usuarios.Select(ToResponse).ToList());
    }

    [HttpGet("usuarios/{id:int}")]
    public async Task<ActionResult<UsuarioAdminResponse>> ObterUsuario(int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var usuario = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
        return usuario is null ? NotFound("Usuário não encontrado.") : Ok(ToResponse(usuario));
    }

    [HttpPost("usuarios")]
    public async Task<ActionResult<UsuarioAdminResponse>> CriarUsuario(
        [FromBody] SalvarUsuarioAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Senha)) return BadRequest("Informe a senha inicial do usuário.");
        var conflito = await ValidarUnicidadeAsync(request, null, cancellationToken);
        if (conflito is not null) return Conflict(conflito);

        var usuario = new User { UserId = db.Users.Select(x => x.UserId).ToList().DefaultIfEmpty(0).Max() + 1 };
        Aplicar(usuario, request);
        usuario.Password = PasswordCipher.Crypt(request.Senha);
        db.Users.Add(usuario);
        await db.SaveChangesAsync(cancellationToken);
        return Created("/api/administracao/usuarios", ToResponse(usuario));
    }

    [HttpPost("apis")]
    public async Task<ActionResult<ApiAdminResponse>> CriarApi(
        [FromBody] CadastrarApiRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        var service = request.Service.Trim();
        var ipv4 = request.Ipv4.Trim();
        if (!IPAddress.TryParse(ipv4, out var endereco) || endereco.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return BadRequest("Informe um endereço IPv4 válido.");

        if (await db.Users.AnyAsync(x => x.Login == service, cancellationToken))
            return Conflict("Já existe uma API ou usuário com esse service/login.");
        if (await db.Users.AnyAsync(x => x.UsernamePc == ipv4, cancellationToken))
            return Conflict("Esse IPv4 já está vinculado a outra API ou usuário.");

        var api = new User
        {
            UserId = db.Users.Select(x => x.UserId).ToList().DefaultIfEmpty(0).Max() + 1,
            Name = request.Nome.Trim().ToUpperInvariant(),
            Login = service,
            Level = "API",
            Password = PasswordCipher.Crypt(request.Password),
            Registration = string.Empty,
            UsernamePc = ipv4
        };

        db.Users.Add(api);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/administracao/usuarios/{api.UserId}", ToApiResponse(api));
    }

    [HttpPut("usuarios/{id:int}")]
    public async Task<ActionResult<UsuarioAdminResponse>> AtualizarUsuario(
        int id, [FromBody] SalvarUsuarioAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var usuario = await db.Users.FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
        if (usuario is null) return NotFound("Usuário não encontrado.");
        var conflito = await ValidarUnicidadeAsync(request, id, cancellationToken);
        if (conflito is not null) return Conflict(conflito);

        Aplicar(usuario, request);
        if (!string.IsNullOrWhiteSpace(request.Senha)) usuario.Password = PasswordCipher.Crypt(request.Senha);
        if (string.IsNullOrWhiteSpace(usuario.Password)) return BadRequest("Informe uma senha para concluir o cadastro.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(usuario));
    }

    [HttpPatch("usuarios/login/{login}/nivel")]
    public async Task<ActionResult<UsuarioAdminResponse>> AtualizarNivelPorLogin(
        string login, [FromBody] AtualizarNivelUsuarioRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();

        login = login.Trim();
        if (string.IsNullOrWhiteSpace(login)) return BadRequest("Login inválido.");

        var linhasAlteradas = await db.Users
            .Where(x => x.Login == login)
            .ExecuteUpdateAsync(
                alteracao => alteracao
                    .SetProperty(x => x.Level, request.Nivel)
                    .SetProperty(x => x.CanPrint, request.PodeImprimir),
                cancellationToken);

        if (linhasAlteradas == 0)
            return NotFound($"Usuário com login {login} não encontrado.");

        var usuario = await db.Users.AsNoTracking()
            .FirstAsync(x => x.Login == login, cancellationToken);
        return Ok(ToResponse(usuario));
    }

    [HttpDelete("usuarios/{id:int}")]
    public async Task<IActionResult> ExcluirUsuario(int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var usuario = await db.Users.FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
        if (usuario is null) return NotFound("Usuário não encontrado.");
        if (usuario.Login == "99999") return BadRequest("O administrador padrão não pode ser excluído.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserPermissions.Where(x => x.UserId == id).ExecuteDeleteAsync(cancellationToken);
        db.Users.Remove(usuario);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("usuarios/{usuarioId:int}/permissoes")]
    public async Task<ActionResult<IReadOnlyCollection<PermissaoAdminResponse>>> Permissoes(
        int usuarioId, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        if (!await db.Users.AnyAsync(x => x.UserId == usuarioId, cancellationToken))
            return NotFound("Usuário não encontrado.");

        return Ok(await db.UserPermissions.AsNoTracking().Where(x => x.UserId == usuarioId)
            .OrderBy(x => x.Module).ThenBy(x => x.Code)
            .Select(x => new PermissaoAdminResponse(x.RegistrationId, x.UserId, x.PermissionId, x.Code,
                x.PermissionName, x.Module, x.Level))
            .ToListAsync(cancellationToken));
    }

    [HttpGet("permissoes/{id:decimal}")]
    public async Task<ActionResult<PermissaoAdminResponse>> ObterPermissao(decimal id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var permissao = await db.UserPermissions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);
        return permissao is null ? NotFound("Permissão não encontrada.") : Ok(ToResponse(permissao));
    }

    [HttpPost("usuarios/{usuarioId:int}/permissoes")]
    public async Task<ActionResult<PermissaoAdminResponse>> CriarPermissao(
        int usuarioId, [FromBody] SalvarPermissaoAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        if (!await db.Users.AnyAsync(x => x.UserId == usuarioId, cancellationToken))
            return NotFound("Usuário não encontrado.");
        if (await ExistePermissaoAsync(usuarioId, request, null, cancellationToken))
            return Conflict("O usuário já possui essa permissão neste módulo.");

        var permissao = new UserPermission
        {
            RegistrationId = db.UserPermissions.Select(x => x.RegistrationId).ToList().DefaultIfEmpty(0).Max() + 1,
            UserId = usuarioId
        };
        Aplicar(permissao, request);
        db.UserPermissions.Add(permissao);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/administracao/permissoes/{permissao.RegistrationId}", ToResponse(permissao));
    }

    [HttpPut("permissoes/{id:decimal}")]
    public async Task<ActionResult<PermissaoAdminResponse>> AtualizarPermissao(
        decimal id, [FromBody] SalvarPermissaoAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var permissao = await db.UserPermissions.FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);
        if (permissao is null) return NotFound("Permissão não encontrada.");
        if (await ExistePermissaoAsync(permissao.UserId, request, id, cancellationToken))
            return Conflict("O usuário já possui essa permissão neste módulo.");

        Aplicar(permissao, request);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(permissao));
    }

    [HttpDelete("permissoes/{id:decimal}")]
    public async Task<IActionResult> ExcluirPermissao(decimal id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var permissao = await db.UserPermissions.FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);
        if (permissao is null) return NotFound("Permissão não encontrada.");
        db.UserPermissions.Remove(permissao);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("impressoras")]
    public async Task<ActionResult<IReadOnlyCollection<ImpressoraAdminResponse>>> Impressoras(CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        return Ok(await db.ImpressorasConfig.AsNoTracking().OrderBy(x => x.NomeImpressora)
            .Select(x => new ImpressoraAdminResponse(x.Id, x.NomeImpressora, x.IpFisico, x.Ativa))
            .ToListAsync(cancellationToken));
    }

    [HttpGet("impressoras/{id:int}")]
    public async Task<ActionResult<ImpressoraAdminResponse>> ObterImpressora(
        int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var impressora = await db.ImpressorasConfig.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return impressora is null
            ? NotFound("Impressora não encontrada.")
            : Ok(ToResponse(impressora));
    }

    [HttpPost("impressoras")]
    public async Task<ActionResult<ImpressoraAdminResponse>> CriarImpressora(
        [FromBody] SalvarImpressoraAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var conflito = await ValidarImpressoraAsync(request, null, cancellationToken);
        if (conflito is not null) return Conflict(conflito);

        var impressora = new ImpressoraConfig();
        Aplicar(impressora, request);
        db.ImpressorasConfig.Add(impressora);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/administracao/impressoras/{impressora.Id}", ToResponse(impressora));
    }

    [HttpPut("impressoras/{id:int}")]
    public async Task<ActionResult<ImpressoraAdminResponse>> AtualizarImpressora(
        int id, [FromBody] SalvarImpressoraAdminRequest request, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var impressora = await db.ImpressorasConfig.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (impressora is null) return NotFound("Impressora não encontrada.");
        var conflito = await ValidarImpressoraAsync(request, id, cancellationToken);
        if (conflito is not null) return Conflict(conflito);

        Aplicar(impressora, request);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(impressora));
    }

    [HttpDelete("impressoras/{id:int}")]
    public async Task<IActionResult> ExcluirImpressora(int id, CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        var impressora = await db.ImpressorasConfig.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (impressora is null) return NotFound("Impressora não encontrada.");
        if (await db.PontosColeta.AnyAsync(x => x.ImpressoraId == id, cancellationToken))
            return Conflict("A impressora está vinculada a um dispositivo e não pode ser excluída.");

        db.ImpressorasConfig.Remove(impressora);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("fila")]
    public async Task<ActionResult<IReadOnlyCollection<DocumentoFilaAdminResponse>>> Fila(CancellationToken cancellationToken)
    {
        if (!EhAdministrador()) return Forbid();
        return Ok(await db.DocumentosRetidos.AsNoTracking().OrderByDescending(x => x.DataEnvio).Take(500)
            .Select(x => new DocumentoFilaAdminResponse(x.Id, x.Username, x.NomeArquivo, x.NumeroPaginas,
                x.DataEnvio, x.Status, x.ImpressoraDestino, x.DadosArquivo.Length))
            .ToListAsync(cancellationToken));
    }

    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyCollection<ApiLogAdminResponse>>> Logs(
        [FromQuery] int quantidade = 300, CancellationToken cancellationToken = default)
    {
        if (!EhAdministrador()) return Forbid();
        quantidade = Math.Clamp(quantidade, 1, 1000);
        return Ok(await db.ApiLogs.AsNoTracking().OrderByDescending(x => x.DataHora).Take(quantidade)
            .Select(x => new ApiLogAdminResponse(x.Id, x.DataHora, x.Nivel, x.Evento, x.Mensagem, x.Endpoint, x.Usuario, x.Detalhes))
            .ToListAsync(cancellationToken));
    }

    private async Task<string?> ValidarUnicidadeAsync(SalvarUsuarioAdminRequest request, int? id, CancellationToken ct)
    {
        var login = request.Login.Trim();
        var matricula = NormalizarRegistro(request.Matricula);
        var usernamePc = NormalizarOuNulo(request.UsernamePc);
        if (await db.Users.AnyAsync(x => x.UserId != id && x.Login == login, ct)) return "Já existe um usuário com esse login.";
        if (await db.Users.AnyAsync(x => x.UserId != id && x.Registration == matricula, ct)) return "Já existe um usuário com essa matrícula.";
        if (usernamePc is not null && await db.Users.AnyAsync(x => x.UserId != id && x.UsernamePc == usernamePc, ct))
            return "Esse username de máquina já está vinculado a outro usuário.";
        return null;
    }

    private async Task<string?> ValidarImpressoraAsync(
        SalvarImpressoraAdminRequest request, int? id, CancellationToken cancellationToken)
    {
        var nome = request.NomeImpressora.Trim();
        var ip = request.IpFisico.Trim();
        if (!IPAddress.TryParse(ip, out var endereco) ||
            endereco.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return "Informe um endereço IPv4 válido.";
        if (await db.ImpressorasConfig.AnyAsync(x => x.Id != id && x.NomeImpressora == nome, cancellationToken))
            return "Já existe uma impressora com esse nome.";
        if (await db.ImpressorasConfig.AnyAsync(x => x.Id != id && x.IpFisico == ip, cancellationToken))
            return "Já existe uma impressora com esse IP físico.";
        return null;
    }

    private static void Aplicar(ImpressoraConfig impressora, SalvarImpressoraAdminRequest request)
    {
        impressora.NomeImpressora = request.NomeImpressora.Trim();
        impressora.IpFisico = request.IpFisico.Trim();
        impressora.Ativa = request.Ativa;
    }

    private static void Aplicar(User usuario, SalvarUsuarioAdminRequest request)
    {
        usuario.Name = request.Nome.Trim().ToUpperInvariant();
        usuario.Login = request.Login.Trim();
        usuario.Registration = NormalizarRegistro(request.Matricula);
        usuario.Level = request.Nivel.Trim();
        usuario.CanPrint = request.PodeImprimir;
        usuario.UsernamePc = NormalizarOuNulo(request.UsernamePc);
    }

    private Task<bool> ExistePermissaoAsync(int usuarioId, SalvarPermissaoAdminRequest request,
        decimal? id, CancellationToken cancellationToken) =>
        db.UserPermissions.AnyAsync(x => x.RegistrationId != id && x.UserId == usuarioId &&
            x.Code == request.Codigo && x.Module == request.Modulo.Trim().ToUpper(), cancellationToken);

    private static void Aplicar(UserPermission permissao, SalvarPermissaoAdminRequest request)
    {
        permissao.PermissionId = request.PermissaoId;
        permissao.Code = request.Codigo;
        permissao.PermissionName = request.Nome.Trim().ToUpperInvariant();
        permissao.Module = request.Modulo.Trim().ToUpperInvariant();
        permissao.Level = request.Nivel;
    }

    private static string? NormalizarOuNulo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var username = valor.Trim();
        var separador = username.LastIndexOf('\\');
        if (separador >= 0 && separador < username.Length - 1) username = username[(separador + 1)..];
        return username.ToLowerInvariant();
    }

    private static string NormalizarRegistro(string? valor)
    {
        var registro = valor?.Trim() ?? string.Empty;
        while (registro.EndsWith(",0", StringComparison.Ordinal) || registro.EndsWith(".0", StringComparison.Ordinal))
            registro = registro[..^2];
        return registro;
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

    private static UsuarioAdminResponse ToResponse(User x) => new(x.UserId, x.Name, x.Login,
        NormalizarRegistro(x.Registration),
        NormalizarNivel(x.Level), x.UsernamePc, x.CanPrint, !string.IsNullOrWhiteSpace(x.Login) && !string.IsNullOrWhiteSpace(x.Password) &&
        (x.Level == "API" ? !string.IsNullOrWhiteSpace(x.UsernamePc) : !string.IsNullOrWhiteSpace(x.Registration)));

    private static string NormalizarNivel(string? nivel) =>
        nivel?.Trim().ToUpperInvariant() switch
        {
            "ADMINISTRADOR" or "ADMIN" => "6",
            "0" or "1" or "2" or "3" or "4" or "5" or "6" => nivel.Trim(),
            _ => "0"
        };
    private static ApiAdminResponse ToApiResponse(User x) =>
        new(x.UserId, x.Name, x.Login!, x.UsernamePc!, x.Level!);
    private static PermissaoAdminResponse ToResponse(UserPermission x) => new(x.RegistrationId, x.UserId,
        x.PermissionId, x.Code, x.PermissionName, x.Module, x.Level);
    private static ImpressoraAdminResponse ToResponse(ImpressoraConfig x) =>
        new(x.Id, x.NomeImpressora, x.IpFisico, x.Ativa);
}

public sealed record SalvarUsuarioAdminRequest
{
    [Required, StringLength(100)] public required string Nome { get; init; }
    [Required, StringLength(100), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "O login deve conter somente letras e números.")]
    public required string Login { get; init; }
    [Required, RegularExpression("^[1-9][0-9]*$", ErrorMessage = "A matrícula deve conter somente números e ser maior que zero.")]
    public required string Matricula { get; init; }
    [Required, RegularExpression("^[0-6]$", ErrorMessage = "O nível deve estar entre 0 e 6.")]
    public required string Nivel { get; init; }
    public bool PodeImprimir { get; init; }
    [StringLength(100)] public string? UsernamePc { get; init; }
    [StringLength(200, MinimumLength = 4), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "A senha deve conter somente letras e números.")]
    public string? Senha { get; init; }
}

public sealed record AtualizarNivelUsuarioRequest
{
    [Required, RegularExpression("^[0-6]$", ErrorMessage = "O nível deve estar entre 0 e 6.")]
    public required string Nivel { get; init; }
    public bool PodeImprimir { get; init; }
}

public sealed record CadastrarApiRequest
{
    [Required, StringLength(100)]
    public required string Nome { get; init; }

    [Required, StringLength(100), RegularExpression("^[A-Za-z0-9._-]+$",
        ErrorMessage = "O service deve conter somente letras, números, ponto, hífen ou sublinhado.")]
    public required string Service { get; init; }

    [Required, StringLength(200, MinimumLength = 4)]
    public required string Password { get; init; }

    [Required, StringLength(15)]
    public required string Ipv4 { get; init; }
}

public sealed record SalvarPermissaoAdminRequest
{
    [Range(typeof(decimal), "1", "79228162514264337593543950335")] public decimal PermissaoId { get; init; }
    [Range(typeof(decimal), "1", "79228162514264337593543950335")] public decimal Codigo { get; init; }
    [Required, StringLength(150)] public required string Nome { get; init; }
    [Required, StringLength(100)] public required string Modulo { get; init; }
    [Range(0, int.MaxValue)] public int Nivel { get; init; }
}

public sealed record SalvarImpressoraAdminRequest
{
    [Required, StringLength(150)] public required string NomeImpressora { get; init; }
    [Required, StringLength(255)] public required string IpFisico { get; init; }
    public bool Ativa { get; init; } = true;
}

public sealed record UsuarioAdminResponse(int UsuarioId, string? Nome, string? Login, string Matricula,
    string? Nivel, string? UsernamePc, bool PodeImprimir, bool CadastroCompleto);
public sealed record ApiAdminResponse(decimal ApiId, string? Nome, string Service, string Ipv4, string Nivel);
public sealed record PermissaoAdminResponse(decimal RegistroId, int UsuarioId, decimal PermissaoId,
    decimal Codigo, string? Nome, string? Modulo, int Nivel);
public sealed record ImpressoraAdminResponse(int Id, string NomeImpressora, string IpFisico, bool Ativa);
public sealed record DocumentoFilaAdminResponse(int Id, string Username, string NomeArquivo, int NumeroPaginas,
    DateTime DataEnvio, string Status, string? ImpressoraDestino, int TamanhoBytes);
public sealed record ApiLogAdminResponse(long Id, DateTime DataHora, string Nivel, string Evento, string Mensagem,
    string? Endpoint, string? Usuario, string? Detalhes);
