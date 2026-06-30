using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cover;

public class DefaultCoverProvider : ICoverProvider
{
    public string ProviderName => "Default";
    public int Priority => 99;

    private static readonly string PlaceholderPath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
        "Assets", "placeholder_cover.jpg");

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        if (File.Exists(PlaceholderPath))
            return Task.FromResult<CoverResult?>(new CoverResult(PlaceholderPath, "image/jpeg", ProviderName));

        return Task.FromResult<CoverResult?>(null);
    }
}
