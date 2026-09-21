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

        // No client-level timeout. Each request carries its own budget, which is what lets the
        // post-processing timeout be raised past what a shared client timeout would allow.
        ForSpeech = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        ForText = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
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
