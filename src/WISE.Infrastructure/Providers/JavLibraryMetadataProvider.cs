using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class JavLibraryMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ILogger<JavLibraryMetadataProvider> _logger;

    private const string BaseUrl = "https://www.javlibrary.com";

    public JavLibraryMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ILogger<JavLibraryMetadataProvider> logger)
    {
        _httpClient = httpClient;
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
        catch (TaskCanceledException ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, "Timeout", sw.Elapsed, exception: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[JavLibrary] Network error");
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[JavLibrary] Unexpected error");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    // JavLibrary の検索: IDで検索 → 単一ヒットで自動リダイレクト or 結果リストから最初のリンクを辿る
    private async Task<string?> ResolveVideoPageUrl(MetadataProviderContext context)
    {
        var searchUrl = $"{BaseUrl}/ja/vl_searchbyid.php?keyword={Uri.EscapeDataString(context.Identifier)}";
        _logger.LogInformation("[JavLibrary] Search={Url}", searchUrl);

        var req = BuildRequest(searchUrl);
        var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("429 Rate Limited");

        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

        if (IsCloudflareChallenge(html))
        {
            _logger.LogWarning("[JavLibrary] Cloudflare protection detected");
            return null;
        }

        // 単一ヒット → レスポンスの最終URL が ?v= を含む
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? searchUrl;
        if (finalUrl.Contains("?v=") || finalUrl.Contains("&v="))
            return finalUrl;

        // 複数ヒット → リストから完全一致の品番リンクを探す
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var idUpper = context.Identifier.ToUpperInvariant();
        var videoLinks = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'video')]//a[contains(@href,'?v=')]");

        if (videoLinks != null)
        {
            foreach (var link in videoLinks)
            {
                var linkText = link.SelectSingleNode(".//div[contains(@class,'id')]")?.InnerText.Trim()
                            ?? link.InnerText.Trim();
                if (linkText.ToUpperInvariant() == idUpper)
                {
                    var href = link.GetAttributeValue("href", "");
                    return href.StartsWith("http") ? href : $"{BaseUrl}/ja/{href.TrimStart('/')}";
                }
            }

            // 完全一致なければ最初のリンク
            var first = videoLinks[0].GetAttributeValue("href", "");
            if (!string.IsNullOrEmpty(first))
                return first.StartsWith("http") ? first : $"{BaseUrl}/ja/{first.TrimStart('/')}";
        }

        return null;
    }

    private async Task<List<MetadataCandidate>> ParseVideoPage(string url, MetadataProviderContext context)
    {
        _logger.LogInformation("[JavLibrary] Parse={Url}", url);

        var req = BuildRequest(url);
        var response = await _httpClient.SendAsync(req, context.CancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

        if (IsCloudflareChallenge(html))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var results = new List<MetadataCandidate>();

        // Title: #video_title h3 の最初のテキストノード（リンクを除いた部分もあるが innerText で十分）
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
            _logger.LogWarning("[JavLibrary] Title not found — possible parser error");
            return results;
        }

        // Cover
        var coverNode = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                     ?? doc.DocumentNode.SelectSingleNode("//div[@id='video_jacket']//img");
        var coverSrc = coverNode?.GetAttributeValue("src", null);
        if (!string.IsNullOrEmpty(coverSrc))
        {
            var coverUrl = coverSrc.StartsWith("//") ? "https:" + coverSrc : coverSrc;
            results.Add(new MetadataCandidate(ProviderId, "Cover", coverUrl, 75, Priority, SourceUrl: url));
        }

        // Info block: #video_info 内の各フィールド
        AddInfoField(doc, results, "video_date",     "ReleaseDate", url);
        AddInfoField(doc, results, "video_length",   "Duration",    url, stripSuffix: "分");
        AddInfoFieldLink(doc, results, "video_maker",   "Maker",  url);
        AddInfoFieldLink(doc, results, "video_label",   "Label",  url);
        AddInfoFieldLink(doc, results, "video_director","Director", url);
        AddInfoFieldLink(doc, results, "video_series",  "Series", url);

        // Genres
        var genreNodes = doc.DocumentNode.SelectNodes("//div[@id='video_genres']//a");
        if (genreNodes != null)
            foreach (var g in genreNodes)
            {
                var genre = System.Net.WebUtility.HtmlDecode(g.InnerText.Trim());
                if (!string.IsNullOrEmpty(genre))
                    results.Add(new MetadataCandidate(ProviderId, "Genre", genre, 75, Priority, SourceUrl: url));
            }

        // Cast
        var castNodes = doc.DocumentNode.SelectNodes("//div[@id='video_cast']//span[contains(@class,'star')]//a");
        if (castNodes != null)
            foreach (var a in castNodes)
            {
                var actress = System.Net.WebUtility.HtmlDecode(a.InnerText.Trim());
                if (!string.IsNullOrEmpty(actress))
                {
                    results.Add(new MetadataCandidate(ProviderId, "Actress", actress, 75, Priority, SourceUrl: url));
                    _logger.LogInformation("[JavLibrary] Actress={Actress}", actress);
                }
            }

        return results;
    }

    private static void AddInfoField(HtmlDocument doc, List<MetadataCandidate> results,
        string divId, string fieldName, string url, string? stripSuffix = null)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//td[@class='text']")
                ?? doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//*[@class='text']");
        var text = node?.InnerText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (stripSuffix != null) text = text.Replace(stripSuffix, "").Trim();
        results.Add(new MetadataCandidate("JavLibrary", fieldName, System.Net.WebUtility.HtmlDecode(text), 75, 45, SourceUrl: url));
    }

    private static void AddInfoFieldLink(HtmlDocument doc, List<MetadataCandidate> results,
        string divId, string fieldName, string url)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//td[@class='text']//a")
                ?? doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']//*[@class='text']//a");
        var text = node?.InnerText.Trim();
        if (!string.IsNullOrEmpty(text))
            results.Add(new MetadataCandidate("JavLibrary", fieldName, System.Net.WebUtility.HtmlDecode(text), 75, 45, SourceUrl: url));
    }

    private static HttpRequestMessage BuildRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        req.Headers.Add("Accept-Language", "ja,en;q=0.9");
        req.Headers.Add("Referer", BaseUrl + "/ja/");
        return req;
    }

    private static bool IsCloudflareChallenge(string html) =>
        html.Contains("cf-browser-verification") ||
        html.Contains("Just a moment") ||
        html.Contains("cloudflare") && html.Contains("challenge");
}
