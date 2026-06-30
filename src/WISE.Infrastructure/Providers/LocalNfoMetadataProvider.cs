using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Data;

namespace WISE.Infrastructure.Providers;

public class LocalNfoMetadataProvider : IMetadataProvider
{
    private readonly MetadataProviderOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalNfoMetadataProvider> _logger;

    public LocalNfoMetadataProvider(
        IOptionsMonitor<MetadataProviderOptions> options,
        IServiceProvider serviceProvider,
        ILogger<LocalNfoMetadataProvider> logger)
    {
        _options = options.Get("LocalNfo") ?? new MetadataProviderOptions { Priority = 40 };
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string ProviderId => "LocalNfo";
    public int Priority => _options.Priority;

    private static readonly string[] CoverSuffixes = { "_cover.jpg", "_cover.png", "-cover.jpg", "-cover.png", ".jpg" };
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi" };

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var results = new List<MetadataCandidate>();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();

            // Assetのファイルパスから作業ディレクトリを特定
            var asset = dbContext.Assets.FirstOrDefault(a => a.WorkId == context.WorkId);
            if (asset == null || string.IsNullOrEmpty(asset.FilePath))
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    "No asset found for this work", sw.Elapsed);
            }

            var dir = Path.GetDirectoryName(asset.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"Directory not found: {dir}", sw.Elapsed);
            }

            _logger.LogInformation("[LocalNfo] Scanning directory={Dir} for {Identifier}", dir, context.Identifier);

            // NFOファイル解析（将来拡張用）
            var nfoPath = Path.Combine(dir, context.Identifier + ".nfo");
            if (File.Exists(nfoPath))
            {
                _logger.LogInformation("[LocalNfo] NFO found: {NfoPath}", nfoPath);
                // TODO: XML NFOパース（Kodi互換）
            }

            // カバー画像を探す
            foreach (var suffix in CoverSuffixes)
            {
                var coverPath = Path.Combine(dir, context.Identifier + suffix);
                if (File.Exists(coverPath))
                {
                    results.Add(new MetadataCandidate(ProviderId, "Cover", coverPath, 100, Priority, SourceUrl: "local"));
                    _logger.LogInformation("[LocalNfo] Cover found: {CoverPath}", coverPath);
                    break;
                }
            }

            // thumb.pngなど標準名のサムネイルも探す
            if (!results.Any(r => r.FieldName == "Cover"))
            {
                foreach (var thumbName in new[] { $"{context.Identifier}-thumb.png", $"{context.Identifier}-thumb.jpg", "thumb.jpg", "thumb.png" })
                {
                    var thumbPath = Path.Combine(dir, thumbName);
                    if (File.Exists(thumbPath))
                    {
                        results.Add(new MetadataCandidate(ProviderId, "Cover", thumbPath, 90, Priority, SourceUrl: "local"));
                        _logger.LogInformation("[LocalNfo] Thumb found: {ThumbPath}", thumbPath);
                        break;
                    }
                }
            }

            sw.Stop();

            if (results.Count == 0)
            {
                _logger.LogInformation("[LocalNfo] No local files found for {Identifier}", context.Identifier);
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    "No local cover or NFO found", sw.Elapsed);
            }

            _logger.LogInformation("[LocalNfo] Found {Count} local metadata items", results.Count);
            await Task.CompletedTask; // 非同期メソッドとして保持
            return MetadataResult.Succeeded(ProviderId, results, sw.Elapsed, strategy: "Local");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[LocalNfo] Error scanning local files");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }
}