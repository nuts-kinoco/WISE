using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Http;

public class CachingHandler : DelegatingHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CachingHandler> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public CachingHandler(IServiceScopeFactory scopeFactory, ILogger<CachingHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get)
            return await base.SendAsync(request, ct);

        var url = request.RequestUri!.ToString();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WiseDbContext>();

        var cached = await db.HttpCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Url == url && c.ExpiresAt > DateTime.UtcNow, ct);

        if (cached != null)
        {
            _logger.LogDebug("[HttpCache] HIT {Url}", url);
            return BuildCachedResponse(cached.Body, cached.ContentType);
        }

        var response = await base.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var now = DateTime.UtcNow;

            var existing = await db.HttpCaches.FirstOrDefaultAsync(c => c.Url == url, ct);
            if (existing != null)
            {
                existing.Body = body;
                existing.ContentType = contentType;
                existing.CachedAt = now;
                existing.ExpiresAt = now.Add(Ttl);
            }
            else
            {
                db.HttpCaches.Add(new HttpCache
                {
                    Url = url,
                    Body = body,
                    ContentType = contentType,
                    CachedAt = now,
                    ExpiresAt = now.Add(Ttl),
                });
            }

            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "[HttpCache] Failed to store {Url}", url); }

            _logger.LogDebug("[HttpCache] STORE {Url}", url);
            response.Content = new StringContent(body, Encoding.UTF8, contentType ?? "text/html");
        }

        return response;
    }

    private static HttpResponseMessage BuildCachedResponse(string body, string? contentType)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Content = new StringContent(body, Encoding.UTF8, contentType ?? "text/html");
        return r;
    }
}
