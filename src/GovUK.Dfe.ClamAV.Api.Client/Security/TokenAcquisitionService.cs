using Azure.Core;
using Azure.Identity;
using GovUK.Dfe.ClamAV.Api.Client.Settings;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace GovUK.Dfe.ClamAV.Api.Client.Security
{
    [ExcludeFromCodeCoverage]
    public class TokenAcquisitionService : ITokenAcquisitionService
    {
        private readonly ClamAvApiClientSettings _settings;
        private readonly ILogger<TokenAcquisitionService> _logger;
        private readonly Lazy<TokenCredential> _credential;

        public TokenAcquisitionService(ClamAvApiClientSettings settings, ILogger<TokenAcquisitionService> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Lazy initialisation so we only build the credential when first used
            _credential = new Lazy<TokenCredential>(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_settings.ClientSecret))
                    {
                        _logger.LogDebug("Using Managed Identity for token acquisition");
                        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
                        {
                            ManagedIdentityClientId = _settings.MiClientId
                        });
                    }

                    _logger.LogDebug("Using Client Credentials (ClientId + Secret) for token acquisition");
                    return new ClientSecretCredential(
                        tenantId: ExtractTenantIdFromAuthority(_settings.Authority),
                        clientId: _settings.ClientId,
                        clientSecret: _settings.ClientSecret);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create TokenCredential");
                    throw;
                }
            });
        }

        public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            try
            {
                var context = new TokenRequestContext(new string[] { _settings.Scope! });
                var token = await _credential.Value.GetTokenAsync(context, cancellationToken);

                if (string.IsNullOrWhiteSpace(token.Token))
                    throw new InvalidOperationException("Token acquisition returned empty access token");

                return token.Token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acquire token for scope {Scope}", _settings.Scope);
                throw;
            }
        }

        private static string ExtractTenantIdFromAuthority(string? authority)
        {
            if (string.IsNullOrWhiteSpace(authority))
                throw new ArgumentNullException(nameof(authority), "Authority URL cannot be null or empty");

            // Example: https://login.microsoftonline.com/{tenantId}/v2.0
            var uri = new Uri(authority);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments.Length > 0 ? segments[0] : throw new FormatException($"Cannot extract tenantId from {authority}");
        }
    }
}
