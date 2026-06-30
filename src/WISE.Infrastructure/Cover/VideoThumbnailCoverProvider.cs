using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Services;

namespace WISE.Infrastructure.Cover;

public class VideoThumbnailCoverProvider : ICoverProvider
{
    private readonly FFmpegThumbnailService _ffmpeg;
    private readonly ILogger<VideoThumbnailCoverProvider> _logger;
    private readonly string _cacheDir;

    public string ProviderName => "VideoThumbnail";
    public int Priority => 30;

    public VideoThumbnailCoverProvider(FFmpegThumbnailService ffmpeg, ILogger<VideoThumbnailCoverProvider> logger)
    {
        _ffmpeg = ffmpeg;
        _logger = logger;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WISE", "covers", "thumbnails");
    }

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
    {
        if (work.MediaType != MediaType.Video) return Task.FromResult(false);
        var hasVideo = work.Assets.Any(a =>
            a.Role == AssetRole.Video && File.Exists(a.FilePath));
        return Task.FromResult(hasVideo);
    }

    public async Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        var videoAsset = work.Assets
            .Where(a => a.Role == AssetRole.Video && File.Exists(a.FilePath))
            .OrderByDescending(a => a.FileSize)
            .FirstOrDefault();

        if (videoAsset == null) return null;

        try
        {
            var thumbnailPath = await _ffmpeg.GenerateThumbnailAsync(videoAsset.FilePath, _cacheDir);
            return new CoverResult(thumbnailPath, "image/jpeg", ProviderName,
                ExpiresAt: DateTime.UtcNow.AddDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VideoThumbnail] Failed to generate thumbnail for work {WorkId}", work.Id);
            return null;
        }
    }
}
