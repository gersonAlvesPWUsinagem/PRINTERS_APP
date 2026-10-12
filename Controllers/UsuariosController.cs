using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize]
public sealed class UsuariosController(AppDbContext db) : ControllerBase
{
    [HttpGet("incompletos")]
    public async Task<IActionResult> Incompletos(CancellationToken cancellationToken)
    {
        var usuarios = await db.Users.AsNoTracking()
            .Where(x => x.UsernamePc != null && (x.Login == null || x.Login == "" || x.Password == null || x.Password == ""))
            .OrderBy(x => x.UsernamePc)
            .Select(x => new
            {
                UsuarioId = x.UserId,
                x.Name,
                x.UsernamePc,
                x.Login,
                CadastroCompleto = false
            })
            .ToListAsync(cancellationToken);
        return Ok(usuarios);
    }
}
