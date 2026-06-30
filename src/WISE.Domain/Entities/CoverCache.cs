using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class CoverCache : Entity
{
    public Guid WorkId { get; private set; }
    public string ProviderName { get; private set; }
    public string CachedPath { get; private set; }
    public string ContentType { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    public virtual Work? Work { get; private set; }

    protected CoverCache()
    {
        ProviderName = string.Empty;
        CachedPath = string.Empty;
        ContentType = string.Empty;
    }

    public CoverCache(Guid workId, string providerName, string cachedPath, string contentType, DateTime? expiresAt = null)
    {
        Id = Guid.NewGuid();
        WorkId = workId;
        ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        CachedPath = cachedPath ?? throw new ArgumentNullException(nameof(cachedPath));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        GeneratedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
