using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PRINTERS_APP.Security;

public sealed class LoginSwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.Name is not ("Login" or "LoginWeb") || operation.RequestBody is null)
            return;

        if (!operation.RequestBody.Content.TryGetValue("application/json", out var jsonContent))
            return;

        jsonContent.Schema = new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string>(StringComparer.Ordinal) { "username", "password" },
            Properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["username"] = new OpenApiSchema { Type = "string", Example = new Microsoft.OpenApi.Any.OpenApiString("99999") },
                ["password"] = new OpenApiSchema { Type = "string", Format = "password", Example = new Microsoft.OpenApi.Any.OpenApiString("99999") }
            }
        };
    }
}
