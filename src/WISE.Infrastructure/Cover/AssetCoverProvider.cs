using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cover;

/// <summary>
/// Resolves cover from an asset already assigned Role=CoverPortrait or CoverLandscape.
/// </summary>
public class AssetCoverProvider : ICoverProvider
{
    public string ProviderName => "AssetCover";
    public int Priority => 10;

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
    {
        var hasCoverAsset = work.Assets.Any(a =>
            (a.Role == AssetRole.CoverPortrait || a.Role == AssetRole.CoverLandscape)
            && File.Exists(a.FilePath));
        return Task.FromResult(hasCoverAsset);
    }

    public Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        var asset = work.Assets
            .Where(a => (a.Role == AssetRole.CoverPortrait || a.Role == AssetRole.CoverLandscape)
                        && File.Exists(a.FilePath))
            .OrderBy(a => a.Role == AssetRole.CoverPortrait ? 0 : 1)
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
