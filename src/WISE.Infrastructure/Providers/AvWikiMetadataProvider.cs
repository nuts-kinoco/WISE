using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// av-wiki.net から日本語メタデータを補完取得するProvider。
/// FANZA/MGSで取得できなかった場合のフォールバック。
/// パッケージ画像はMGStage CDN (image.mgstage.com/pb_p_...) から高解像度で取得。
/// URL例: https://av-wiki.net/abw-123/
/// FANZA(80)/MGS(70) より Priority が低いため、公式が成功している項目は上書きされない。
/// </summary>
public class AvWikiMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AvWikiMetadataProvider> _logger;

    public AvWikiMetadataProvider(HttpClient httpClient, ILogger<AvWikiMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderId => "AvWiki";
    public int Priority => 30; // Tier4: AdultWiki と並列実行
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var slug = context.Identifier.ToLower();
            var url = $"https://av-wiki.net/{slug}/";
            _logger.LogInformation("[AvWiki] Fetching {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "ja-JP,ja;q=0.9,en;q=0.8");
            request.Headers.Add("Referer", "https://av-wiki.net/");

            var response = await _httpClient.SendAsync(request, context.CancellationToken);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[AvWiki] HTTP {Status} for {Url}", response.StatusCode, url);
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"HTTP {(int)response.StatusCode}", sw.Elapsed);
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            var candidates = new List<MetadataCandidate>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // --- Title ---
            // blockquote-like の <p> が最もクリーンなタイトル
            // 形式: 【ABW-123】顔射の美学 15... → 品番プレフィックスを除去
            var titleNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'blockquote-like')]/p")
                ?? doc.DocumentNode.SelectSingleNode("//h1[@class='entry-title']")
                ?? doc.DocumentNode.SelectSingleNode("//h1");
            if (titleNode != null)
            {
                var raw = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());
                // 【ABW-123】 or ABW-123 プレフィックス除去
                var title = Regex.Replace(raw, @"^【[^】]+】\s*", "").Trim();
                title = Regex.Replace(title, @"^[A-Za-z]+-\d+\s*", "").Trim();
                // "に出てるAV女優は誰？ 名前は？" などサイト固有のサフィックス除去（og:titleには付くがh1には付かない）
                if (!string.IsNullOrWhiteSpace(title))
                {
                    candidates.Add(new MetadataCandidate(ProviderId, "Title", title, 70, Priority, SourceUrl: url));
                    _logger.LogInformation("[AvWiki] Title={Title}", title);
                }
            }

            // --- Info table: <dl class="dltable"><dt>key</dt><dd>value</dd> ---
            // dt を直接反復して次の兄弟 dd を取得する
            var dtNodes = doc.DocumentNode.SelectNodes("//dl[contains(@class,'dltable')]/dt")
                       ?? doc.DocumentNode.SelectNodes("//dl/dt");

            var genreList = new List<string>();

            if (dtNodes != null)
            {
                foreach (var dt in dtNodes)
                {
                    // 次の要素ノードが dd であることを確認
                    var dd = dt.NextSibling;
                    while (dd != null && dd.NodeType != HtmlAgilityPack.HtmlNodeType.Element) dd = dd.NextSibling;
                    if (dd == null || dd.Name != "dd") continue;

                    var key = System.Net.WebUtility.HtmlDecode(dt.InnerText.Trim());
                    var rawValue = System.Net.WebUtility.HtmlDecode(dd.InnerText.Trim());
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(rawValue)) continue;

                    _logger.LogInformation("[AvWiki] Row: {Key} = {Val}", key, rawValue);

                    if ((key.Contains("女優") || key.Contains("出演") || key == "AV女優名"))
                    {
                        var links = dd.SelectNodes(".//a");
                        IEnumerable<string> names = links != null
                            ? links.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText.Trim()))
                            : rawValue.Split(new[] { '\n', '、', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim());
                        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
                        {
                            candidates.Add(new MetadataCandidate(ProviderId, "Actress", name, 75, Priority, SourceUrl: url));
                            _logger.LogInformation("[AvWiki] Actress={Name}", name);
                        }
                    }
                    else if (key.Contains("メーカー") && !candidates.Any(c => c.FieldName == "Maker"))
                    {
                        var a = dd.SelectSingleNode(".//a");
                        var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
                        if (!string.IsNullOrWhiteSpace(val))
                            candidates.Add(new MetadataCandidate(ProviderId, "Maker", val, 70, Priority, SourceUrl: url));
                    }
                    else if (key.Contains("レーベル") && !candidates.Any(c => c.FieldName == "Label"))
                    {
                        var a = dd.SelectSingleNode(".//a");
                        var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
                        if (!string.IsNullOrWhiteSpace(val))
                            candidates.Add(new MetadataCandidate(ProviderId, "Label", val, 65, Priority, SourceUrl: url));
                    }
                    else if (key.Contains("シリーズ") && !candidates.Any(c => c.FieldName == "Series"))
                    {
                        var a = dd.SelectSingleNode(".//a");
                        var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
                        if (!string.IsNullOrWhiteSpace(val))
                            candidates.Add(new MetadataCandidate(ProviderId, "Series", val, 65, Priority, SourceUrl: url));
                    }
                    else if ((key.Contains("発売日") || key.Contains("配信開始")) && !candidates.Any(c => c.FieldName == "ReleaseDate"))
                    {
                        var m = Regex.Match(rawValue, @"\d{4}[-/]\d{2}[-/]\d{2}");
                        if (m.Success)
                            candidates.Add(new MetadataCandidate(ProviderId, "ReleaseDate", m.Value.Replace('/', '-'), 70, Priority, SourceUrl: url));
                    }
                    else if (key.Contains("収録時間") && !candidates.Any(c => c.FieldName == "Runtime"))
                    {
                        var m = Regex.Match(rawValue, @"(\d+)");
                        if (m.Success)
                            candidates.Add(new MetadataCandidate(ProviderId, "Runtime", m.Value, 65, Priority, SourceUrl: url));
                    }
                    else if (key.Contains("タグ") || key.Contains("ジャンル"))
                    {
                        var tagLinks = dd.SelectNodes(".//a");
                        var tags = tagLinks != null
                            ? tagLinks.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText.Trim()))
                            : rawValue.Split(new[] { '\n', '、', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());
                        foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t) && !genreList.Contains(t)))
                            genreList.Add(tag);
                    }
                }
            }

            if (genreList.Count > 0 && !candidates.Any(c => c.FieldName == "Genre"))
                candidates.Add(new MetadataCandidate(ProviderId, "Genre", string.Join("|", genreList), 65, Priority, SourceUrl: url));

            // --- Cover image ---
            // 優先1: MGStage CDN の高解像度パッケージ画像 (pb_p_ = portrait package)
            string? coverSrc = null;
            var mgsCover = doc.DocumentNode.SelectSingleNode("//img[contains(@src,'image.mgstage.com') and contains(@src,'pb_p')]");
            if (mgsCover != null)
                coverSrc = mgsCover.GetAttributeValue("src", null);

            // 優先2: wp-post-image クラス → article内img
            if (string.IsNullOrWhiteSpace(coverSrc))
            {
                var coverImg = doc.DocumentNode.SelectSingleNode("//img[contains(@class,'wp-post-image')]")
                    ?? doc.DocumentNode.SelectSingleNode("//article//img[@src]")
                    ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'entry-content')]//img[@src]");
                coverSrc = coverImg?.GetAttributeValue("src", null) ?? coverImg?.GetAttributeValue("data-src", null);
            }

            if (!string.IsNullOrWhiteSpace(coverSrc) && coverSrc!.StartsWith("http"))
            {
                candidates.Add(new MetadataCandidate(ProviderId, "PortraitCover", coverSrc, 65, Priority, SourceUrl: url));
                _logger.LogInformation("[AvWiki] Cover={Src}", coverSrc);
            }

            if (candidates.Count == 0)
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No candidates extracted", sw.Elapsed);

            _logger.LogInformation("[AvWiki] Extracted {Count} candidates: {Fields}",
                candidates.Count, string.Join(", ", candidates.Select(c => $"{c.FieldName}={c.Value}")));

            return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
        }
        catch (TaskCanceledException ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[AvWiki] Network error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[AvWiki] Error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }
}
