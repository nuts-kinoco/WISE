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
using WISE.Infrastructure.Cookies;

namespace WISE.Infrastructure.Providers;

public class MgsMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ICookieProvider _cookieProvider;
    private readonly ILogger<MgsMetadataProvider> _logger;

    public MgsMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ICookieProvider cookieProvider,
        ILogger<MgsMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("Mgs") ?? new MetadataProviderOptions { Priority = 70 };
        _cookieProvider = cookieProvider;
        _logger = logger;
    }

    public string ProviderId => "Mgs";
    public int Priority => _options.Priority;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    // MGStageはFC2作品を取り扱わない。識別子を直接URL化＋検索フォールバックする実装のため、
    // 除外しないとFC2識別子で無関係な検索結果を弱くマッチしてしまう恐れがある。
    public bool CanHandle(string identifier) =>
        !identifier.StartsWith("FC2", StringComparison.OrdinalIgnoreCase);

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var results = new List<MetadataCandidate>();
        var policy = _cookieProvider.GetPolicy(ProviderId);
        string cookieHeader = policy?.GetCookieHeader() ?? string.Empty;

        try
        {
            string url = $"https://www.mgstage.com/product/product_detail/{context.Identifier.ToUpper()}/";
            _logger.LogInformation("[Mgs] Strategy=Http | URL={Url}", url);

            var response = await SendWithCookieAsync(url, cookieHeader, context);

            if (!response.IsSuccessStatusCode)
            {
                // 検索フォールバック
                string searchUrl = $"https://www.mgstage.com/search/search.php?search_word={Uri.EscapeDataString(context.Identifier)}&search_field=1";
                _logger.LogInformation("[Mgs] Fallback search URL={Url}", searchUrl);

                var searchResponse = await SendWithCookieAsync(searchUrl, cookieHeader, context);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    sw.Stop();
                    return MetadataResult.Failed(ProviderId, FailureReason.Network,
                        $"HTTP {response.StatusCode}", sw.Elapsed);
                }

                var searchHtml = await searchResponse.Content.ReadAsStringAsync(context.CancellationToken);

                // 年齢確認検出
                if (searchHtml.Contains("年齢認証") || searchHtml.Contains("18歳未満"))
                {
                    sw.Stop();
                    _logger.LogWarning("[Mgs] AgeVerification detected. Cookie={Cookie}", cookieHeader);
                    return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                        "Age verification page detected. Cookie may be expired.", sw.Elapsed);
                }

                var searchDoc = new HtmlDocument();
                searchDoc.LoadHtml(searchHtml);
                var firstLink = searchDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'search_list')]//a");
                if (firstLink == null)
                {
                    sw.Stop();
                    return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                        $"No search result for {context.Identifier}", sw.Elapsed);
                }

                url = "https://www.mgstage.com" + firstLink.GetAttributeValue("href", "");
                response = await SendWithCookieAsync(url, cookieHeader, context);
                if (!response.IsSuccessStatusCode)
                {
                    sw.Stop();
                    return MetadataResult.Failed(ProviderId, FailureReason.Network,
                        $"Detail HTTP {response.StatusCode}", sw.Elapsed);
                }
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            sw.Stop();

            // 年齢確認ページ検出
            if (html.Contains("年齢認証") || html.Contains("18歳未満"))
            {
                _logger.LogWarning("[Mgs] AgeVerification detected on detail page. FutureStrategy=Browser");
                return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                    "Age verification page. Future: Browser fallback.", sw.Elapsed);
            }

            _logger.LogInformation("[Mgs] HTTP OK | Elapsed={Elapsed}ms", sw.ElapsedMilliseconds);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@class='tag']")
                ?? doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'tag')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'title')]//h1")
                ?? doc.DocumentNode.SelectSingleNode("//h1");
            string? titleText = null;
            if (titleNode != null)
            {
                titleText = titleNode.InnerText.Trim();
            }
            else
            {
                var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
                titleText = ogTitle?.GetAttributeValue("content", null)?.Trim();
            }

            if (!string.IsNullOrEmpty(titleText))
            {
                results.Add(new MetadataCandidate(ProviderId, "Title", titleText, 80, Priority, SourceUrl: url));
                _logger.LogInformation("[Mgs] Title={Title}", titleText);
            }
            else
            {
                _logger.LogWarning("[Mgs] Title not found (ParserError)");
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError,
                    "Title element not found.", sw.Elapsed);
            }

            // Cover
            var coverNode = doc.DocumentNode.SelectSingleNode("//div[@class='detail_photo']/a/img") ??
                            doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
            if (coverNode != null)
            {
                var coverUrl = coverNode.Name == "meta"
                    ? coverNode.GetAttributeValue("content", "")
                    : coverNode.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    results.Add(new MetadataCandidate(ProviderId, "PortraitCover", coverUrl, 80, Priority, SourceUrl: url));
                    _logger.LogInformation("[Mgs] Cover={CoverUrl}", coverUrl);
                }
            }

            // 詳細情報テーブル
            var infoRows = doc.DocumentNode.SelectNodes("//tr[@class='tr_block']") ??
                           doc.DocumentNode.SelectNodes("//div[@class='detail_data']//tr");
            if (infoRows != null)
            {
                foreach (var row in infoRows)
                {
                    var th = row.SelectSingleNode("th");
                    var td = row.SelectSingleNode("td");
                    if (th == null || td == null) continue;

                    string header = th.InnerText.Trim();
                    string data = td.InnerText.Trim();

                    if (header.Contains("出演") || header.Contains("女優"))
                    {
                        var links = td.SelectNodes("a");
                        if (links != null)
                            foreach (var a in links)
                            {
                                var name = a.InnerText.Trim();
                                // "すべて表示する" などのUI操作リンクを除外
                                if (string.IsNullOrWhiteSpace(name) || name.Contains("表示する") || name.Contains("もっと見る"))
                                    continue;
                                results.Add(new MetadataCandidate(ProviderId, "Actress", name, 80, Priority, SourceUrl: url));
                                _logger.LogInformation("[Mgs] Actress={Name}", name);
                            }
                        else if (!string.IsNullOrEmpty(data))
                            results.Add(new MetadataCandidate(ProviderId, "Actress", data, 80, Priority, SourceUrl: url));
                    }
                    else if (header.Contains("メーカー"))
                    {
                        var a = td.SelectSingleNode("a");
                        var maker = a?.InnerText.Trim() ?? data;
                        if (!string.IsNullOrEmpty(maker))
                        {
                            results.Add(new MetadataCandidate(ProviderId, "Maker", maker, 80, Priority, SourceUrl: url));
                            _logger.LogInformation("[Mgs] Maker={Maker}", maker);
                        }
                    }
                    else if (header.Contains("収録時間"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(data, @"\d+");
                        if (match.Success)
                            results.Add(new MetadataCandidate(ProviderId, "Runtime", match.Value, 80, Priority, SourceUrl: url));
                    }
                    else if (header.Contains("発売日") || header.Contains("配信開始"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(data, @"\d{4}/\d{2}/\d{2}");
                        if (match.Success)
                            results.Add(new MetadataCandidate(ProviderId, "ReleaseDate", match.Value, 80, Priority, SourceUrl: url));
                    }
                    else if (header.Contains("シリーズ"))
                    {
                        var a = td.SelectSingleNode("a");
                        if (a != null)
                            results.Add(new MetadataCandidate(ProviderId, "Series", a.InnerText.Trim(), 80, Priority, SourceUrl: url));
                    }
                    else if (header.Contains("レーベル"))
                    {
                        var a = td.SelectSingleNode("a");
                        if (a != null)
                        {
                            results.Add(new MetadataCandidate(ProviderId, "Label", a.InnerText.Trim(), 80, Priority, SourceUrl: url));
                        }
                    }
                    else if (header.Contains("ジャンル"))
                    {
                        var genres = td.SelectNodes("a");
                        if (genres != null)
                            foreach (var g in genres)
                                results.Add(new MetadataCandidate(ProviderId, "Genre", g.InnerText.Trim(), 80, Priority, SourceUrl: url));
                    }
                }
            }

            _logger.LogInformation("[Mgs] MetadataStatus=Success | Fields={Count}", results.Count);
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
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[Mgs] Unexpected error");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private Task<HttpResponseMessage> SendWithCookieAsync(string url, string cookieHeader, MetadataProviderContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return _httpClient.SendAsync(request, context.CancellationToken);
    }
}