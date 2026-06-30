using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cover;

/// <summary>
/// Falls back to first downloaded sample image when no jacket cover is available.
/// Priority=25: after ArchiveCoverProvider (20), before VideoThumbnailCoverProvider (30).
/// </summary>
public class SampleImageCoverProvider : ICoverProvider
{
    public string ProviderName => "SampleImage";
    public int Priority => 25;

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
    {
        var has = work.Assets.Any(a => a.Role == AssetRole.Sample && File.Exists(a.FilePath));
        return Task.FromResult(has);
    }

    public Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        var asset = work.Assets
            .Where(a => a.Role == AssetRole.Sample && File.Exists(a.FilePath))
            .OrderBy(a => a.OriginalFilename)
            .FirstOrDefault();

        if (asset == null) return Task.FromResult<CoverResult?>(null);

        var contentType = Path.GetExtension(asset.FilePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };

        return Task.FromResult<CoverResult?>(new CoverResult(asset.FilePath, contentType, ProviderName));
    }
}
