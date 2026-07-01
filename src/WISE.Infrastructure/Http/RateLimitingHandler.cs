namespace WISE.Infrastructure.Http;

public class RateLimitingHandler : DelegatingHandler
{
    private readonly RateLimiterService _rateLimiter;

    public RateLimitingHandler(RateLimiterService rateLimiter)
    {
        _rateLimiter = rateLimiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var domain = request.RequestUri?.Host ?? "";
        if (!string.IsNullOrEmpty(domain))
            await _rateLimiter.AcquireAsync(domain, ct);

        return await base.SendAsync(request, ct);
    }
}
