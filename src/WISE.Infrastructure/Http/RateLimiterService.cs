using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WISE.Infrastructure.Http;

public class RateLimiterService
{
    private readonly ConcurrentDictionary<string, BucketState> _buckets = new();
    private readonly RateLimiterOptions _options;
    private readonly ILogger<RateLimiterService> _logger;

    public RateLimiterService(IConfiguration config, ILogger<RateLimiterService> logger)
    {
        _options = config.GetSection("RateLimiter").Get<RateLimiterOptions>() ?? new RateLimiterOptions();
        _logger = logger;
    }

    public async Task AcquireAsync(string domain, CancellationToken ct)
    {
        var bucket = _buckets.GetOrAdd(domain, _ => CreateBucket(domain));

        await bucket.Lock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - bucket.LastRefill).TotalSeconds;
            bucket.Tokens = Math.Min(bucket.MaxTokens, bucket.Tokens + elapsed * bucket.RefillRate);
            bucket.LastRefill = now;

            if (bucket.Tokens >= 1.0)
            {
                bucket.Tokens -= 1.0;
                return;
            }

            var waitSeconds = (1.0 - bucket.Tokens) / bucket.RefillRate;
            var delay = TimeSpan.FromSeconds(waitSeconds);
            _logger.LogDebug("[RateLimit] {Domain}: waiting {Delay:N2}s for token", domain, delay.TotalSeconds);

            await Task.Delay(delay, ct);

            now = DateTime.UtcNow;
            elapsed = (now - bucket.LastRefill).TotalSeconds;
            bucket.Tokens = Math.Min(bucket.MaxTokens, bucket.Tokens + elapsed * bucket.RefillRate);
            bucket.LastRefill = now;
            bucket.Tokens -= 1.0;
        }
        finally
        {
            bucket.Lock.Release();
        }
    }

    private BucketState CreateBucket(string domain)
    {
        var (rate, burst) = _options.GetDomainConfig(domain);
        return new BucketState
        {
            Tokens = burst,
            MaxTokens = burst,
            RefillRate = rate,
            LastRefill = DateTime.UtcNow,
        };
    }
}

internal sealed class BucketState
{
    public SemaphoreSlim Lock = new(1, 1);
    public double Tokens;
    public double MaxTokens;
    public double RefillRate;
    public DateTime LastRefill;
}

public sealed class RateLimiterOptions
{
    public double DefaultRequestsPerSecond { get; set; } = 2.0;
    public double DefaultBurstSize { get; set; } = 5;
    public Dictionary<string, DomainRateLimitConfig> Domains { get; set; } = new();

    public (double rate, double burst) GetDomainConfig(string domain)
    {
        if (Domains.TryGetValue(domain, out var cfg))
            return (cfg.RequestsPerSecond, cfg.BurstSize);

        // parent domain match (e.g. "fc2.com" matches "video.fc2.com")
        foreach (var (key, val) in Domains)
        {
            if (domain.EndsWith("." + key, StringComparison.OrdinalIgnoreCase))
                return (val.RequestsPerSecond, val.BurstSize);
        }

        return (DefaultRequestsPerSecond, DefaultBurstSize);
    }
}

public sealed class DomainRateLimitConfig
{
    public double RequestsPerSecond { get; set; } = 2.0;
    public double BurstSize { get; set; } = 5;
}
