using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Siniestros360.ServiceDefaults.Endpoints;

// Declara el esquema JWT Bearer en el documento OpenAPI (equivalente nativo del AddSwaggerGenWithAuth de la plantilla,
// sin Swashbuckle) y lo exige en toda operación: la política por defecto de la API pide token salvo AllowAnonymous.
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemes) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (!(await schemes.GetAllSchemesAsync()).Any(scheme => scheme.Name == "Bearer")) return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", In = ParameterLocation.Header, BearerFormat = "JWT" },
        };
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations ?? []))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        }
    }
}
