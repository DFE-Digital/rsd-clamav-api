using GovUK.Dfe.ClamAV.Api.Client.Security;
using GovUK.Dfe.ClamAV.Api.Client.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace GovUK.Dfe.ClamAV.Api.Client.Extensions
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddClamAvApiClient<TClientInterface, TClientImplementation>(
            this IServiceCollection services,
            IConfiguration configuration,
            HttpClient? existingHttpClient = null)
            where TClientInterface : class
            where TClientImplementation : class, TClientInterface
        {
            var apiSettings = new ClamAvApiClientSettings();
            configuration.GetSection("ClamAvApiClient").Bind(apiSettings);

            services.AddSingleton(apiSettings);
            services.AddSingleton<ITokenAcquisitionService, TokenAcquisitionService>();

            if (existingHttpClient != null)
            {
                services.AddSingleton(existingHttpClient);
                services.AddTransient<TClientInterface, TClientImplementation>(serviceProvider =>
                {
                    return ActivatorUtilities.CreateInstance<TClientImplementation>(
                        serviceProvider, existingHttpClient, apiSettings.BaseUrl!);
                });
            }
            else
            {
                services.AddHttpClient<TClientInterface, TClientImplementation>((httpClient, serviceProvider) =>
                    {
                        httpClient.BaseAddress = new Uri(apiSettings.BaseUrl!);

                        return ActivatorUtilities.CreateInstance<TClientImplementation>(
                            serviceProvider, httpClient, apiSettings.BaseUrl!);
                    })
                    .AddHttpMessageHandler(serviceProvider =>
                    {
                        var tokenService = serviceProvider.GetRequiredService<ITokenAcquisitionService>();
                        return new BearerTokenHandler(tokenService);
                    });
            }
            return services;
        }
    }
}
