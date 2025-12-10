using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace GovUK.Dfe.ClamAV.Api.Client.Security
{
    [ExcludeFromCodeCoverage]
    public class BearerTokenHandler(ITokenAcquisitionService tokenAcquisitionService, ILogger<BearerTokenHandler> logger) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var token = await tokenAcquisitionService.GetTokenAsync(cancellationToken);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            logger.LogDebug("ClamAV API Token: " + token);

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
