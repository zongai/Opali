using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace Opaline.Core.Net;

/// <summary>
/// Shared HttpClient creation with TLS 1.2/1.3, decompression, and sane pooling.
/// Mitigates intermittent "SSL connection could not be established" on Windows.
/// </summary>
public static class AppHttp
{
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 16,
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(45),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
    }

    public static async Task<T> WithSslRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default,
        int attempts = 2)
    {
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSsl(ex) && i < attempts - 1)
            {
                last = ex;
                await Task.Delay(400 * (i + 1), ct).ConfigureAwait(false);
            }
        }
        throw last ?? new InvalidOperationException("request failed");
    }

    public static bool IsTransientSsl(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var m = e.Message ?? "";
            if (m.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || m.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || m.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || e is System.IO.IOException
                || e is HttpRequestException)
                return true;
        }
        return false;
    }
}
