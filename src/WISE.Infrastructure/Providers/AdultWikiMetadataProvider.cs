using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// adult-wiki.net から JAV メタデータを取得する Tier-2 フォールバックプロバイダー。
/// FANZA / MGS / AvWiki で取得できない品番（例: RPIN 系）のカバレッジ補完を目的とする。
///
/// URL形式: https://adult-wiki.net/details/?pno={prefix}{5桁ゼロ埋め数字}
/// 例: RPIN-010 → pno=rpin00010
///
/// Priority=30 なので Fanza(80)/Mgs(70)/Fc2(60)/AvWiki(60) が取得済みのフィールドは上書きしない。
/// </summary>
public class AdultWikiMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AdultWikiMetadataProvider> _logger;

    public AdultWikiMetadataProvider(HttpClient httpClient, ILogger<AdultWikiMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderId => "AdultWiki";
    public int Priority => 30;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var pno = ToPno(context.Identifier);
            if (pno == null)
            {
                _logger.LogDebug("[AdultWiki] 品番変換スキップ: {Id}", context.Identifier);
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    "Identifier format not supported", sw.Elapsed);
            }

            // adult-wiki.net は pno だけでは 404。検索ページから詳細URL を抽出する必要がある。
            var searchUrl = $"https://adult-wiki.net/search/?keyword={pno}";
            var detailUrl = await ResolveDetailUrlAsync(searchUrl, pno, context.CancellationToken);
            if (detailUrl == null)
            {
                _logger.LogInformation("[AdultWiki] 検索結果から詳細URLが見つからない: {Pno}", pno);
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    "Detail page not found in search results", sw.Elapsed);
            }

            _logger.LogInformation("[AdultWiki] Fetch {Url}", detailUrl);

            var req = new HttpRequestMessage(HttpMethod.Get, detailUrl);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "ja-JP,ja;q=0.9,en;q=0.8");
            req.Headers.Add("Referer", "https://adult-wiki.net/");

            var response = await _httpClient.SendAsync(req, context.CancellationToken);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[AdultWiki] HTTP {Status} {Url}", response.StatusCode, detailUrl);
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"HTTP {(int)response.StatusCode}", sw.Elapsed);
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            var candidates = ParseHtml(html, detailUrl);

            if (candidates.Count == 0)
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError,
                    "No candidates extracted", sw.Elapsed);

            _logger.LogInformation("[AdultWiki] Extracted {Count} candidates: {Fields}",
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
            _logger.LogWarning(ex, "[AdultWiki] Network error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[AdultWiki] Unexpected error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private List<MetadataCandidate> ParseHtml(string html, string url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var results = new List<MetadataCandidate>();

        // タイトル: <h1> または <title> タグ（品番プレフィックスを除去）
        var titleNode = doc.DocumentNode.SelectSingleNode("//h1")
                     ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
        if (titleNode != null)
        {
            var raw = titleNode.Name == "meta"
                ? titleNode.GetAttributeValue("content", "")
                : System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());

            // 【RPIN-010】 / RPIN-010 などのプレフィックスを除去
            raw = Regex.Replace(raw, @"^【[^】]+】\s*", "").Trim();
            raw = Regex.Replace(raw, @"^[A-Za-z]+-?\d+\s+", "").Trim();
            // "の無修正動画" などのサイト固有サフィックスを除去
            raw = Regex.Replace(raw, @"\s*(の|は|が).{0,20}(動画|女優|AV|無修正).*$", "").Trim();

            if (!string.IsNullOrWhiteSpace(raw) && raw.Length > 2)
            {
                results.Add(new MetadataCandidate(ProviderId, "Title", raw, 65, Priority, SourceUrl: url));
                _logger.LogInformation("[AdultWiki] Title={Title}", raw);
            }
        }

        // 情報テーブル: <table> / <dl> / <div class="..."> 形式の複数パターンに対応
        ParseInfoTable(doc, results, url);

        // カバー画像: og:image → 記事内 img の順
        var ogImage = doc.DocumentNode
            .SelectSingleNode("//meta[@property='og:image']")
            ?.GetAttributeValue("content", null);
        if (!string.IsNullOrWhiteSpace(ogImage) && ogImage!.StartsWith("http"))
        {
            results.Add(new MetadataCandidate(ProviderId, "PortraitCover", ogImage, 60, Priority, SourceUrl: url));
            _logger.LogInformation("[AdultWiki] Cover={Src}", ogImage);
        }
        else
        {
            var imgNode = doc.DocumentNode.SelectSingleNode("//article//img[@src]")
                       ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'entry')]//img[@src]")
                       ?? doc.DocumentNode.SelectSingleNode("//main//img[@src]");
            var src = imgNode?.GetAttributeValue("src", null) ?? imgNode?.GetAttributeValue("data-src", null);
            if (!string.IsNullOrWhiteSpace(src) && src!.StartsWith("http"))
            {
                results.Add(new MetadataCandidate(ProviderId, "PortraitCover", src, 55, Priority, SourceUrl: url));
                _logger.LogInformation("[AdultWiki] Cover(fallback)={Src}", src);
            }
        }

        return results;
    }

    private void ParseInfoTable(HtmlDocument doc, List<MetadataCandidate> results, string url)
    {
        var genreList = new List<string>();

        // パターン1: <table> の <th>/<td> ペア
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var th = row.SelectSingleNode(".//th");
                var td = row.SelectSingleNode(".//td");
                if (th == null || td == null) continue;
                var key = System.Net.WebUtility.HtmlDecode(th.InnerText.Trim());
                ProcessKeyValue(key, td, results, genreList, url);
            }
        }

        // パターン2: <dl> の <dt>/<dd> ペア
        var dtNodes = doc.DocumentNode.SelectNodes("//dl/dt");
        if (dtNodes != null)
        {
            foreach (var dt in dtNodes)
            {
                var dd = dt.NextSibling;
                while (dd != null && dd.NodeType != HtmlAgilityPack.HtmlNodeType.Element)
                    dd = dd.NextSibling;
                if (dd?.Name != "dd") continue;
                var key = System.Net.WebUtility.HtmlDecode(dt.InnerText.Trim());
                ProcessKeyValue(key, dd, results, genreList, url);
            }
        }

        // パターン3: "label: value" 形式の <p> / <div> スパン
        var labelSpans = doc.DocumentNode.SelectNodes(
            "//*[contains(@class,'label') or contains(@class,'key') or contains(@class,'name')]");
        if (labelSpans != null)
        {
            foreach (var span in labelSpans)
            {
                var valueNode = span.NextSibling;
                while (valueNode != null && valueNode.NodeType != HtmlAgilityPack.HtmlNodeType.Element)
                    valueNode = valueNode.NextSibling;
                if (valueNode == null) continue;
                var key = System.Net.WebUtility.HtmlDecode(span.InnerText.Trim());
                ProcessKeyValue(key, valueNode, results, genreList, url);
            }
        }

        if (genreList.Count > 0 && !results.Any(c => c.FieldName == "Genre"))
            results.Add(new MetadataCandidate(ProviderId, "Genre",
                string.Join("|", genreList), 55, Priority, SourceUrl: url));
    }

    private void ProcessKeyValue(
        string key, HtmlAgilityPack.HtmlNode valueNode,
        List<MetadataCandidate> results, List<string> genreList, string url)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var rawValue = System.Net.WebUtility.HtmlDecode(valueNode.InnerText.Trim());
        if (string.IsNullOrWhiteSpace(rawValue)) return;

        _logger.LogDebug("[AdultWiki] Row: {Key} = {Val}", key, rawValue);

        if (key.Contains("女優") || key.Contains("出演") || key == "AV女優名" || key.Contains("キャスト"))
        {
            var links = valueNode.SelectNodes(".//a");
            var names = links != null
                ? links.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText.Trim()))
                : rawValue.Split(new[] { '\n', '、', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim());
            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                results.Add(new MetadataCandidate(ProviderId, "Actress", name, 70, Priority, SourceUrl: url));
                _logger.LogInformation("[AdultWiki] Actress={Name}", name);
            }
        }
        else if ((key.Contains("メーカー") || key.Contains("制作")) && !results.Any(c => c.FieldName == "Maker"))
        {
            var a = valueNode.SelectSingleNode(".//a");
            var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
            if (!string.IsNullOrWhiteSpace(val))
                results.Add(new MetadataCandidate(ProviderId, "Maker", val, 65, Priority, SourceUrl: url));
        }
        else if (key.Contains("レーベル") && !results.Any(c => c.FieldName == "Label"))
        {
            var a = valueNode.SelectSingleNode(".//a");
            var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
            if (!string.IsNullOrWhiteSpace(val))
                results.Add(new MetadataCandidate(ProviderId, "Label", val, 60, Priority, SourceUrl: url));
        }
        else if (key.Contains("シリーズ") && !results.Any(c => c.FieldName == "Series"))
        {
            var a = valueNode.SelectSingleNode(".//a");
            var val = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? rawValue).Trim());
            if (!string.IsNullOrWhiteSpace(val))
                results.Add(new MetadataCandidate(ProviderId, "Series", val, 60, Priority, SourceUrl: url));
        }
        else if ((key.Contains("発売") || key.Contains("配信")) && !results.Any(c => c.FieldName == "ReleaseDate"))
        {
            var m = Regex.Match(rawValue, @"\d{4}[-/]\d{2}[-/]\d{2}");
            if (m.Success)
                results.Add(new MetadataCandidate(ProviderId, "ReleaseDate",
                    m.Value.Replace('/', '-'), 65, Priority, SourceUrl: url));
        }
        else if ((key.Contains("収録") || key.Contains("時間") || key.Contains("再生")) && !results.Any(c => c.FieldName == "Duration"))
        {
            var m = Regex.Match(rawValue, @"(\d+)");
            if (m.Success)
                results.Add(new MetadataCandidate(ProviderId, "Duration", m.Value, 55, Priority, SourceUrl: url));
        }
        else if (key.Contains("タグ") || key.Contains("ジャンル") || key.Contains("カテゴリ"))
        {
            var tagLinks = valueNode.SelectNodes(".//a");
            var tags = tagLinks != null
                ? tagLinks.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText.Trim()))
                : rawValue.Split(new[] { '\n', '、', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());
            foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t) && !genreList.Contains(t)))
                genreList.Add(tag);
        }
    }

    // 検索ページから詳細URLを抽出（adult-wiki.net は pno だけでは 404 になるため）
    private async Task<string?> ResolveDetailUrlAsync(string searchUrl, string pno, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            var html = await res.Content.ReadAsStringAsync(ct);

            // 検索結果から詳細URLを抽出: href="/details/?actress=...&pno=rpin00010"
            var m = Regex.Match(html, @"href=""(/details/\?[^""]*pno=" + Regex.Escape(pno) + @"[^""]*)""");
            if (m.Success)
            {
                var path = m.Groups[1].Value;
                return "https://adult-wiki.net" + System.Net.WebUtility.HtmlDecode(path);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // RPIN-010 → rpin00010  |  ABW-123 → abw00123  |  SONE-001 → sone00001
    // FC2-PPV-4823194 などの複合識別子は対象外（null を返す）
    internal static string? ToPno(string identifier)
    {
        var m = Regex.Match(identifier.Trim(), @"^([A-Za-z]{2,8})-(\d{1,6})$");
        if (!m.Success) return null;
        var prefix = m.Groups[1].Value.ToLower();
        if (!int.TryParse(m.Groups[2].Value, out var number)) return null;
        return $"{prefix}{number:D5}";
    }
}
