using System.Collections.Generic;
using System.Linq;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cookies;

/// <summary>
/// ProviderIdに基づいて適切なICookiePolicyを解決・提供します。
/// </summary>
public interface ICookieProvider
{
    ICookiePolicy? GetPolicy(string providerId);
}

public class CookieProvider : ICookieProvider
{
    private readonly IEnumerable<ICookiePolicy> _policies;

    public CookieProvider(IEnumerable<ICookiePolicy> policies)
    {
        _policies = policies ?? Enumerable.Empty<ICookiePolicy>();
    }

    public ICookiePolicy? GetPolicy(string providerId)
    {
        return _policies.FirstOrDefault(p => p.ProviderId == providerId);
    }
}
