using System.Net;

namespace WayType.Services;

/// <summary>
/// Owns the two long-lived HTTP clients so endpoint latency budgets stay separate per feature.
/// </summary>
public sealed class WayTypeHttp : IDisposable
{
    private readonly HttpMessageHandler _handler;

    public WayTypeHttp()
    {
        _handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        ForSpeech = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        ForText = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
    }

    public HttpClient ForSpeech { get; }

    public HttpClient ForText { get; }

    public void Dispose()
    {
        ForSpeech.Dispose();
        ForText.Dispose();
        _handler.Dispose();
    }
}
