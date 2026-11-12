using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GovUK.Dfe.ClamAV.Swagger
{
    public class AuthenticationHeaderOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", context.Document), new List<string>()
                }
            });
        }
    }
}
