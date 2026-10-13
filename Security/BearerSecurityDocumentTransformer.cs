using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LogiTrack.Security;

public sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the accessToken returned by /api/auth/login."
        };
        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith("/api/inventory") && !path.StartsWith("/api/orders")) continue;
            if (item.Operations is null) continue;
            foreach (var operation in item.Operations.Values)
                operation.Security = [new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                }];
        }
        return Task.CompletedTask;
    }
}
