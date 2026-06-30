using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Archive;

namespace WISE.Infrastructure.Cover;

public class ArchiveCoverProvider : ICoverProvider
{
    private readonly ArchiveReaderSelector _selector;
    private readonly ILogger<ArchiveCoverProvider> _logger;
    private readonly string _cacheDir;

    public string ProviderName => "ArchiveCover";
    public int Priority => 20;

    public ArchiveCoverProvider(ArchiveReaderSelector selector, ILogger<ArchiveCoverProvider> logger)
    {
        _selector = selector;
        _logger = logger;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WISE", "covers", "archive");
    }

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
    {
        if (work.MediaType is not (MediaType.Comic or MediaType.Book or MediaType.PhotoBook or MediaType.ImageCollection))
            return Task.FromResult(false);

        var archiveAsset = work.Assets.FirstOrDefault(a =>
            (a.StorageFormat == StorageFormat.Archive || a.StorageFormat == StorageFormat.Folder)
            && _selector.Select(a.FilePath) != null);

        return Task.FromResult(archiveAsset != null);
    }

    public async Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        var archiveAsset = work.Assets.FirstOrDefault(a =>
            (a.StorageFormat == StorageFormat.Archive || a.StorageFormat == StorageFormat.Folder)
            && _selector.Select(a.FilePath) != null);

        if (archiveAsset == null) return null;

        var reader = _selector.Select(archiveAsset.FilePath)!;

        try
        {
            var pages = await reader.GetPagesAsync(archiveAsset.FilePath, ct);
            if (pages.Count == 0) return null;

            var firstPage = pages[0];
            if (firstPage.ContentType == "application/pdf") return null; // PDF defers to client

            Directory.CreateDirectory(_cacheDir);
            var ext = firstPage.ContentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
            var cachePath = Path.Combine(_cacheDir, $"{work.Id}{ext}");

            if (!File.Exists(cachePath))
            {
                using var stream = await reader.OpenPageAsync(archiveAsset.FilePath, 0, ct);
                using var fs = File.Create(cachePath);
                await stream.CopyToAsync(fs, ct);
            }

            return new CoverResult(cachePath, firstPage.ContentType, ProviderName,
                ExpiresAt: DateTime.UtcNow.AddDays(7));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ArchiveCover] Failed to extract cover from {Path}", archiveAsset.FilePath);
            return null;
        }
    }
}
