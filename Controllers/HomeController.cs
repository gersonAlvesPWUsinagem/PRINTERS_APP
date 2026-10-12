using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PRINTERS_APP.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class HomeController(IHostEnvironment environment) : ControllerBase
{
    [HttpGet("/")]
    [HttpGet("/api/home")]
    [ProducesResponseType<HomeResponse>(StatusCodes.Status200OK)]
    public ActionResult<HomeResponse> Index()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "Não informada";

        return Ok(new HomeResponse(
            Nome: "PRINTERS_APP",
            Descricao: "API de retenção, autenticação e liberação de documentos para impressão.",
            Versao: version,
            Ambiente: environment.EnvironmentName,
            Status: "Online",
            DataHoraServidor: DateTimeOffset.Now));
    }
}

public sealed record HomeResponse(
    string Nome,
    string Descricao,
    string Versao,
    string Ambiente,
    string Status,
    DateTimeOffset DataHoraServidor);
