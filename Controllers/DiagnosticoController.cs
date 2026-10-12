using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;

namespace PRINTERS_APP.Controllers;

[ApiController]
[Route("api/diagnostico")]
[Authorize]
public sealed class DiagnosticoController(AppDbContext db) : ControllerBase
{
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int quantidade = 200, CancellationToken cancellationToken = default)
    {
        quantidade = Math.Clamp(quantidade, 1, 1000);
        var logs = await db.ApiLogs.AsNoTracking()
            .OrderByDescending(x => x.DataHora)
            .Take(quantidade)
            .ToListAsync(cancellationToken);
        return Ok(logs);
    }
}
