using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Data.Models;
using WISE.Infrastructure.Http;

namespace WISE.Infrastructure.Providers;

// Cloudflare の JS チャレンジを Playwright で突破し、HTMLキャッシュ（HttpCaches テーブル）を経由する。
// HttpClient 版は恒常的にブロックされるため完全に置き換え。
public class JavLibraryMetadataProvider : IMetadataProvider
{
    private readonly PlaywrightBrowserService _playwright;
    private readonly RateLimiterService _rateLimiter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetadataProviderOptions _options;
    private readonly ILogger<JavLibraryMetadataProvider> _logger;

    private const string BaseUrl = "https://www.javlibrary.com";
    private const string JavavLibraryDomain = "www.javlibrary.com";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public JavLibraryMetadataProvider(
        PlaywrightBrowserService playwright,
        RateLimiterService rateLimiter,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<MetadataProviderOptions> options,
        ILogger<JavLibraryMetadataProvider> logger)
    {
        _playwright = playwright;
        _rateLimiter = rateLimiter;
        _scopeFactory = scopeFactory;
        _options = options.Get("JavLibrary") ?? new MetadataProviderOptions { Priority = 45 };
        _logger = logger;
    }

    public string ProviderId => "JavLibrary";
    public int Priority => _options.Priority;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        try
        {
            var videoPageUrl = await ResolveVideoPageUrl(context);
            if (videoPageUrl == null)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"{context.Identifier} not found", sw.Elapsed);
            }

            var results = await ParseVideoPage(videoPageUrl, context);
            sw.Stop();

            if (results.Count == 0)
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No fields extracted", sw.Elapsed);

            _logger.LogInformation("[JavLibrary] Success | Fields={Count}", results.Count);
            return MetadataResult.Succeeded(ProviderId, results, sw.Elapsed);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, "Canceled", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[JavLibrary] Unexpected error");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    // IDで検索: 単一ヒット時は #video_title が直接表示、複数ヒット時は div.video のリストになる。
    private async Task<string?> ResolveVideoPageUrl(MetadataProviderContext context)
    {
        var searchUrl = $"{BaseUrl}/ja/vl_searchbyid.php?keyword={Uri.EscapeDataString(context.Identifier)}";
        _logger.LogInformation("[JavLibrary] Search={Url}", searchUrl);

        var html = await FetchWithCacheAsync(searchUrl, "#video_title, div.video", context.CancellationToken);
        if (html == null) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 単一ヒット: #video_title が存在する
        if (doc.DocumentNode.SelectSingleNode("//div[@id='video_title']") != null)
            return searchUrl;

        // 複数ヒット: 品番完全一致のリンクを探す
        var idUpper = context.Identifier.ToUpperInvariant();
        var videoLinks = doc.DocumentNode.SelectNodes("//div[contains(@class,'video')]//a[contains(@href,'?v=')]");
        if (videoLinks == null) return null;

        foreach (var link in videoLinks)
        {
            var idText = link.SelectSingleNode(".//div[contains(@class,'id')]")?.InnerText.Trim()
                      ?? link.InnerText.Trim();
            if (idText.ToUpperInvariant() == idUpper)
                return NormalizeHref(link.GetAttributeValue("href", ""));
        }

        // 完全一致なければ先頭
        var firstHref = videoLinks[0].GetAttributeValue("href", "");
        return string.IsNullOrEmpty(firstHref) ? null : NormalizeHref(firstHref);
    }

    private async Task<List<MetadataCandidate>> ParseVideoPage(string url, MetadataProviderContext context)
    {
        _logger.LogInformation("[JavLibrary] Parse={Url}", url);

        var html = await FetchWithCacheAsync(url, "#video_title", context.CancellationToken);
        if (html == null) return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var results = new List<MetadataCandidate>();

        var titleNode = doc.DocumentNode.SelectSingleNode("//div[@id='video_title']//h3");
        if (titleNode != null)
        {
            var title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());
            if (!string.IsNullOrEmpty(title))
            {
                results.Add(new MetadataCandidate(ProviderId, "Title", title, 75, Priority, SourceUrl: url));
                _logger.LogInformation("[JavLibrary] Title={Title}", title);
            }
        }

        if (results.Count == 0)
        {
            _logger.LogWarning("[JavLibrary] Title not found — HTML parse mismatch?");
            return results;
        }

        var coverNode = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                     ?? doc.DocumentNode.SelectSingleNode("//div[@id='video_jacket']//img");
        var coverSrc = coverNode?.GetAttributeValue("src", null);
        if (!string.IsNullOrEmpty(coverSrc))
        {
            var coverUrl = coverSrc.StartsWith("//") ? "https:" + coverSrc : coverSrc;
            results.Add(new MetadataCandidate(ProviderId, "Cover", coverUrl, 75, Priority, SourceUrl: url));
        }

        AddInfoField(doc, results, "video_date",      "ReleaseDate", url);
        AddInfoField(doc, results, "video_length",    "Duration",    url, stripSuffix: "分");
        AddInfoFieldLink(doc, results, "video_maker",    "Maker",    url);
        AddInfoFieldLink(doc, results, "video_label",    "Label",    url);
        AddInfoFieldLink(doc, results, "video_director", "Director", url);
        AddInfoFieldLink(doc, results, "video_series",   "Series",   url);

        var genreNodes = doc.DocumentNode.SelectNodes("//div[@id='video_genres']//a");
        if (genreNodes != null)
            foreach (var g in genreNodes)
            {
                var genre = System.Net.WebUtility.HtmlDecode(g.InnerText.Trim());
                if (!string.IsNullOrEmpty(genre))
                    results.Add(new MetadataCandidate(ProviderId, "Genre", genre, 75, Priority, SourceUrl: url));
            }

        var castNodes = doc.DocumentNode.SelectNodes("//div[@id='video_cast']//span[contains(@class,'star')]//a");
        if (castNodes != null)
            foreach (var a in castNodes)
            {
                var actress = System.Net.WebUtility.HtmlDecode(a.InnerText.Trim());
                if (!string.IsNullOrEmpty(actress))
                    results.Add(new MetadataCandidate(ProviderId, "Actress", actress, 75, Priority, SourceUrl: url));
            }

        return results;
    }

    // キャッシュ確認 → ミスなら rate limit → Playwright fetch → キャッシュ保存
    private async Task<string?> FetchWithCacheAsync(string url, string waitForSelector, System.Threading.CancellationToken ct)
    {
        // キャッシュ確認
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WiseDbContext>();
            var cached = await db.HttpCaches
                .FirstOrDefaultAsync(c => c.Url == url && c.ExpiresAt > DateTime.UtcNow, ct);
            if (cached != null)
            {
                _logger.LogDebug("[JavLibrary] Cache hit {Url}", url);
                return cached.Body;
            }
        }

        // Playwright fetch (with rate limit)
        await _rateLimiter.AcquireAsync(JavavLibraryDomain, ct);
        var html = await _playwright.FetchHtmlAsync(url, waitForSelector, ct);
        if (html == null) return null;

        // キャッシュ保存
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WiseDbContext>();
            var entry = new HttpCache
            {
                Url = url,
                Body = html,
                ContentType = "text/html",
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(CacheTtl),
            };
            db.HttpCaches.Add(entry);
            try { await db.SaveChangesAsync(ct); }
            catch { /* UNIQUE 制約違反は並行 insert — 無視してよい */ }
        }

        return html;
    }

    private string NormalizeHref(string href)
    {
        if (string.IsNullOrEmpty(href)) return href;
        if (href.StartsWith("http")) return href;
        return $"{BaseUrl}/ja/{href.TrimStart('/')}";
    }

    private static void AddInfoField(HtmlDocument doc, List<MetadataCandidate> results,
        string divId, string fieldName, string url, string? stripSuffix = null)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//td[@class='text']")
                ?? doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//*[@class='text']");
        var text = node?.InnerText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (stripSuffix != null) text = text.Replace(stripSuffix, "").Trim();
        results.Add(new MetadataCandidate("JavLibrary", fieldName,
            System.Net.WebUtility.HtmlDecode(text), 75, 45, SourceUrl: url));
    }

    private static void AddInfoFieldLink(HtmlDocument doc, List<MetadataCandidate> results,
        string divId, string fieldName, string url)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//td[@class='text']//a")
                ?? doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//*[@class='text']//a");
        var text = node?.InnerText.Trim();
        if (!string.IsNullOrEmpty(text))
            results.Add(new MetadataCandidate("JavLibrary", fieldName,
                System.Net.WebUtility.HtmlDecode(text), 75, 45, SourceUrl: url));
    }
}
