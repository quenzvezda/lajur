using Microsoft.AspNetCore.HttpOverrides;

namespace Lajur.Api.Web;

/// <summary>
/// Honors X-Forwarded-For/Proto only from configured proxy networks (loopback is always trusted).
/// Behind the host Nginx this restores the real client IP for per-IP login rate limits and the
/// https scheme for redirects/cookies. Untrusted senders cannot spoof either header.
/// </summary>
public static class ReverseProxySetup
{
    public const string TrustedNetworksKey = "Lajur:ReverseProxy:TrustedNetworks";

    public static IServiceCollection AddLajurReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection(TrustedNetworksKey).Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var network in networks.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network.Trim()));
            }
        });
        return services;
    }
}
