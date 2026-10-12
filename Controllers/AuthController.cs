using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PRINTERS_APP.Models;
using PRINTERS_APP.Repositories;
using PRINTERS_APP.Services;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    IAuthService auth,
    IPermissionRepository permissions,
    TokenRevocationService revocations) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("auth-web")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginWeb([FromBody] LoginAppRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);

        //if (result.Success && result.Value is not null)
        //    Response.Cookies.Append("AuthTokenPRINTERS", result.Value.Token, new CookieOptions
        //    {
        //        HttpOnly = true,
        //        Secure = Request.IsHttps,
        //        SameSite = SameSiteMode.Strict,
        //        Expires = DateTimeOffset.UtcNow.AddDays(360)
        //    });

        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("logout-web")]
    [AllowAnonymous]
    public IActionResult LogoutWeb()
    {
        Response.Cookies.Delete("AuthTokenPRINTERS");
        return NoContent();
    }

    [HttpPost("logout-service")]
    [Authorize]
    public async Task<IActionResult> LogoutService(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(User.FindFirstValue("Service"))) return Forbid();

        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        var token = authorization[7..].Trim();
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();

        await revocations.RevokeAsync(token, cancellationToken);
        return NoContent();
    }

    [HttpGet("get-permission-user")]
    [Authorize]
    public async Task<IActionResult> GetPermissionUser([FromQuery] int module, [FromQuery] int permission, CancellationToken cancellationToken)
    {
        //var userId = decimal.Parse(User.FindFirstValue("IdAdi")!);
        //var moduleName = module switch
        //{
        //    1 => "COMERCIAL",
        //    2 => "FINANCEIRO",
        //    3 => "QUALIDADE",
        //    4 => "ENGENHARIA",
        //    5 => "MANUFATURA",
        //    6 => "SUPRIMENTOS",
        //    _ => "PADRÃO / NÃO DEFINIDO"
        //};
        //var result = await permissions.FindAsync(userId, permission, moduleName, cancellationToken);
        //var value = result.Select(x => new PermissionResponse(x.Level, module, (int)x.PermissionId, x.UserId)).ToList();
        //return Ok(new ApiResponse<List<PermissionResponse>>(value.Count > 0, value.Count > 0 ? "OK permissão encontrada para o usuário." : "Não foram encontradas permissões para este usuário.", value.Count > 0 ? 200 : 404, value));
        return Ok("Teeste");
    }
}
