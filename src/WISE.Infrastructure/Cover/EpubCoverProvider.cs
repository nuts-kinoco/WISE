using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Archive;

namespace WISE.Infrastructure.Cover;

public class EpubCoverProvider : ICoverProvider
{
    private readonly ILogger<EpubCoverProvider> _logger;
    private readonly string _cacheDir;

    public string ProviderName => "EpubCover";
    public int Priority => 15; // after AssetCover(10), before ArchiveCover(20)

    public EpubCoverProvider(ILogger<EpubCoverProvider> logger)
    {
        _logger = logger;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WISE", "covers", "epub");
    }

    public Task<bool> CanHandleAsync(Work work, CancellationToken ct = default)
    {
        var hasEpub = work.Assets.Any(a =>
            a.StorageFormat == StorageFormat.Epub
            && File.Exists(a.FilePath));
        return Task.FromResult(hasEpub);
    }

    public async Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default)
    {
        var epubAsset = work.Assets.FirstOrDefault(a =>
            a.StorageFormat == StorageFormat.Epub && File.Exists(a.FilePath));

        if (epubAsset == null) return null;

        try
        {
            Directory.CreateDirectory(_cacheDir);
            var cachePath = Path.Combine(_cacheDir, $"{work.Id}.jpg");

            if (File.Exists(cachePath))
                return new CoverResult(cachePath, "image/jpeg", ProviderName, DateTime.UtcNow.AddDays(7));

            using var zip = ZipFile.OpenRead(epubAsset.FilePath);
            var imagePath = EpubArchiveReader.GetCoverImagePath(zip);
            if (imagePath == null) return null;

            var entry = zip.GetEntry(imagePath)
                ?? zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(imagePath, StringComparison.OrdinalIgnoreCase));

            if (entry == null) return null;

            var contentType = GetContentType(imagePath);
            var ext = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
            cachePath = Path.Combine(_cacheDir, $"{work.Id}{ext}");

            using var entryStream = entry.Open();
            using var fs = File.Create(cachePath);
            await entryStream.CopyToAsync(fs, ct);

            return new CoverResult(cachePath, contentType, ProviderName, DateTime.UtcNow.AddDays(7));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EpubCover] Failed to extract cover from {Path}", epubAsset.FilePath);
            return null;
        }
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
}
