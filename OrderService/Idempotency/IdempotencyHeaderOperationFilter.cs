using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderService.Idempotency;

/// <summary>
/// Swagger operation filter that adds an Idempotency-Key header parameter
/// to any endpoint decorated with <see cref="IdempotentRequestAttribute"/>.
/// </summary>
public class IdempotencyHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasIdempotentAttribute = context.MethodInfo
            .GetCustomAttributes(typeof(IdempotentRequestAttribute), inherit: true)
            .Length > 0;

        if (!hasIdempotentAttribute)
            return;

        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = "A unique idempotency key (e.g. a GUID). " +
                          "Duplicate keys within 24 hours replay the original response.",
            Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
        });
    }
}