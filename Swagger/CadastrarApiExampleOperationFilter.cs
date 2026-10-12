using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using PRINTERS_APP.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PRINTERS_APP.Swagger;

public sealed class CadastrarApiExampleOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.GetParameters().Any(parameter => parameter.ParameterType == typeof(CadastrarApiRequest)) ||
            operation.RequestBody is null)
            return;

        var example = new OpenApiObject
        {
            ["nome"] = new OpenApiString("PRINTERS_WEB"),
            ["service"] = new OpenApiString("printers.web"),
            ["password"] = new OpenApiString("altere-esta-senha"),
            ["ipv4"] = new OpenApiString("192.168.0.235")
        };

        foreach (var mediaType in operation.RequestBody.Content.Values)
            mediaType.Example = example;
    }
}
