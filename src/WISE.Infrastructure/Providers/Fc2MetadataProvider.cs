using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Cookies;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// FC2コンテンツマーケットからメタデータを取得する。
/// URL: https://adult.contents.fc2.com/article/{numericId}/
/// 年齢確認Cookie は ICookieProvider 経由で注入。
/// %APPDATA%\WISE\fc2StorageState.json または fc2Cookies.txt に配置する。
/// </summary>
public class Fc2MetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ICookieProvider _cookieProvider;
    private readonly ILogger<Fc2MetadataProvider> _logger;

    public Fc2MetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ICookieProvider cookieProvider,
        ILogger<Fc2MetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("Fc2") ?? new MetadataProviderOptions { Priority = 60 };
        _cookieProvider = cookieProvider;
        _logger = logger;
    }

    public string ProviderId => "Fc2";
    public int Priority => _options.Priority;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    // FC2-PPV-XXXXXXX 形式の識別子のみ処理する
    public bool CanHandle(string identifier) =>
        Regex.IsMatch(identifier, @"^FC2", RegexOptions.IgnoreCase);

    // FC2 は構造的に女優(Actress)を持たず、販売者(Maker)も欠落しがち。
    // 早期終了判定では Title のみを「確実に供給可能」と宣言する
    // （Maker/Genre/ReleaseDate/Cover は取得できれば保存するが、揃うのを待たない）。
    public IReadOnlySet<string>? ProvidableFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title" };

    private static readonly string[] NotFoundPhrases =
    {
        "申し訳ありません、お探しの商品が見つかりませんでした",
        "ご指定のページは見つかりません",
        "404 Not Found"
    };

    // 通常ページにも「年齢確認」という文言は現れるため、
    // og:title が空の状態でさらにこれらが含まれる場合のみゲートと判定する
    private static readonly string[] AgeGatePhrases =
    {
        "年齢認証", "18歳未満", "adult check", "age verification"
    };

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var candidates = new List<MetadataCandidate>();

        try
        {
            // FC2のIDを数字部分のみ抽出 (FC2-PPV-4409072 → 4409072)
            var idMatch = Regex.Match(context.Identifier, @"(?:FC2-PPV-|FC2-)(\d+)", RegexOptions.IgnoreCase);
            if (!idMatch.Success)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"Cannot extract numeric ID from {context.Identifier}", sw.Elapsed);
            }

            var policy = _cookieProvider.GetPolicy(ProviderId);
            string cookieHeader = policy?.GetCookieHeader() ?? string.Empty;

            string numericId = idMatch.Groups[1].Value;
            string url = $"https://adult.contents.fc2.com/article/{numericId}/";
            _logger.LogInformation("[Fc2] Fetching {Url} | Cookie={HasCookie}", url, !string.IsNullOrEmpty(cookieHeader));

            var response = await SendWithCookieAsync(url, cookieHeader, context);

            if (!response.IsSuccessStatusCode)
            {
                sw.Stop();
                _logger.LogWarning("[Fc2] HTTP {Status} for {Url}", response.StatusCode, url);
                return MetadataResult.Failed(ProviderId, FailureReason.Network,
                    $"HTTP {response.StatusCode}", sw.Elapsed);
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            sw.Stop();

            foreach (var phrase in NotFoundPhrases)
            {
                if (html.Contains(phrase))
                {
                    _logger.LogInformation("[Fc2] NotFound for {Id}", context.Identifier);
                    return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                        $"Product not found: {phrase}", sw.Elapsed);
                }
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 年齢確認ゲート検出: og:title が空かつゲートキーワードが存在する場合のみ
            // （通常ページにも「年齢確認」文言が含まれるため og:title 空を必須条件とする）
            var ogTitleForGate = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
                                    ?.GetAttributeValue("content", "") ?? "";
            if (string.IsNullOrWhiteSpace(ogTitleForGate))
            {
                foreach (var phrase in AgeGatePhrases)
                {
                    if (html.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[Fc2] AgeVerification gate detected for {Id}. Place cookies in %APPDATA%\\WISE\\fc2StorageState.json", context.Identifier);
                        return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                            "Age verification page detected. Place FC2 session cookies in %APPDATA%\\WISE\\fc2StorageState.json", sw.Elapsed);
                    }
                }
            }

            ExtractMetadata(doc, url, candidates);

            if (!candidates.Any(c => c.FieldName == "Title"))
            {
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "Title not found", sw.Elapsed);
            }

            _logger.LogInformation("[Fc2] OK | Fields={Fields} Elapsed={Ms}ms",
                string.Join(",", candidates.Select(c => c.FieldName)), sw.ElapsedMilliseconds);
            return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
        }
        catch (TaskCanceledException ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, "Timeout", sw.Elapsed, exception: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[Fc2] Network error");
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[Fc2] Unexpected error");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private Task<HttpResponseMessage> SendWithCookieAsync(string url, string cookieHeader, MetadataProviderContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.Add("Accept-Language", "ja,en;q=0.9");
        return _httpClient.SendAsync(request, context.CancellationToken);
    }

    /// <summary>HAP で解析した HtmlDocument からメタデータを抽出する（テスト可能な純粋関数）。</summary>
    internal static void ExtractMetadata(HtmlDocument doc, string sourceUrl, List<MetadataCandidate> candidates)
    {
        const string provider = "Fc2";
        const int priority = 60;

        // --- Title (og:title 優先、なければ title タグ) ---
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
                         ?.GetAttributeValue("content", "");
        var titleFallback = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
        var title = (string.IsNullOrWhiteSpace(ogTitle) ? titleFallback : ogTitle) ?? "";
        title = title.Replace(" - FC2コンテンツマーケット", "")
                     .Replace(" | FC2-PPV", "")
                     .Trim();
        // 先頭の品番プレフィックスを除去: "FC2-PPV-NNNNNNN " or "FC2-NNNNNNN "
        title = Regex.Replace(title, @"^FC2-(?:PPV-)?\d+\s*", "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(title))
            candidates.Add(new MetadataCandidate(provider, "Title", title, 80, priority, SourceUrl: sourceUrl));

        // --- PortraitCover (og:image) ---
        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                        ?.GetAttributeValue("content", "");
        if (!string.IsNullOrWhiteSpace(ogImage))
            candidates.Add(new MetadataCandidate(provider, "PortraitCover", ogImage!, 80, priority, SourceUrl: sourceUrl));

        // --- SampleImages (items_article_SampleImages section の a[href]) ---
        // href は //contents-thumbnail2.fc2.com/w1280/storage... 形式
        // → https://storage... に変換して SampleImage 候補として追加
        var sampleLinks = doc.DocumentNode
            .SelectNodes("//section[contains(@class,'items_article_SampleImages')]//a[@data-image-slideshow]");
        if (sampleLinks != null)
        {
            foreach (var a in sampleLinks)
            {
                var href = a.GetAttributeValue("href", "");
                if (string.IsNullOrWhiteSpace(href)) continue;

                var fullUrl = NormalizeThumbnailUrl(href);
                if (fullUrl != null)
                    candidates.Add(new MetadataCandidate(provider, "SampleImage", fullUrl, 75, priority, SourceUrl: sourceUrl));
            }
        }

        // --- Tags → Genre ("|" 区切りで1候補に束ねる) ---
        var tagNodes = doc.DocumentNode.SelectNodes("//a[contains(@class,'tagTag')]");
        if (tagNodes != null && tagNodes.Count > 0)
        {
            var tags = tagNodes
                .Select(n => System.Net.WebUtility.HtmlDecode(n.InnerText.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();
            if (tags.Count > 0)
                candidates.Add(new MetadataCandidate(provider, "Genre", string.Join("|", tags), 75, priority, SourceUrl: sourceUrl));
        }

        // --- ReleaseDate (販売日 : YYYY/MM/DD) ---
        var softDivs = doc.DocumentNode.SelectNodes("//div[contains(@class,'items_article_softDevice')]/p");
        if (softDivs != null)
        {
            foreach (var p in softDivs)
            {
                var text = p.InnerText.Trim();
                if (text.Contains("販売日"))
                {
                    var dateMatch = Regex.Match(text, @"(\d{4}/\d{2}/\d{2})");
                    if (dateMatch.Success)
                    {
                        candidates.Add(new MetadataCandidate(provider, "ReleaseDate",
                            dateMatch.Groups[1].Value, 80, priority, SourceUrl: sourceUrl));
                    }
                    break;
                }
            }
        }

        // --- Maker (販売者 = FC2 ユーザー名) ---
        // <li>by <a href="https://adult.contents.fc2.com/users/username/">username</a>
        var sellerNode = doc.DocumentNode
            .SelectSingleNode("//a[contains(@href,'/users/')]");
        if (sellerNode != null)
        {
            var seller = System.Net.WebUtility.HtmlDecode(sellerNode.InnerText.Trim());
            if (!string.IsNullOrWhiteSpace(seller))
                candidates.Add(new MetadataCandidate(provider, "Maker", seller, 70, priority, SourceUrl: sourceUrl));
        }
    }

    /// <summary>
    /// FC2 thumbnail CDN URL を元の storage URL に変換する。
    /// 例: //contents-thumbnail2.fc2.com/w1280/storage200000... → https://storage200000...
    /// </summary>
    internal static string? NormalizeThumbnailUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // プロトコル補完
        if (url.StartsWith("//")) url = "https:" + url;

        // w{N}/ プレフィックスを含む CDN URL をストレージ URL に変換
        var match = Regex.Match(url,
            @"https?://contents-thumbnail\d*\.fc2\.com/\w+/(?<storage>storage\d+\.contents\.fc2\.com/.+)");
        if (match.Success)
            return "https://" + match.Groups["storage"].Value;

        // 既に storage URL ならそのまま返す
        if (url.Contains("storage") && url.Contains("contents.fc2.com"))
            return url;

        return null;
    }
}
