using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// FC2コンテンツマーケットが削除済みの場合にフォールバックする代替スクレイパー。
/// MissAV → bestjavporn → javdock → 123AV の順でフォールバックして取得する。
/// Priority=55 (Fc2=60 より低く、競合時は公式が優先される)
/// </summary>
public class Fc2AltMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Fc2AltMetadataProvider> _logger;

    public Fc2AltMetadataProvider(HttpClient httpClient, ILogger<Fc2AltMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderId => "Fc2Alt";
    public int Priority => 55;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    // Fc2MetadataProvider と同様、早期終了判定では Title のみを確実供給とみなす。
    public IReadOnlySet<string>? ProvidableFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title" };

    private static readonly string[] BrowserUserAgent = [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
    ];

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var sw = Stopwatch.StartNew();

        var idMatch = Regex.Match(context.Identifier ?? "", @"(?:FC2-PPV-|FC2-)(\d+)", RegexOptions.IgnoreCase);
        if (!idMatch.Success)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                $"Not an FC2 identifier: {context.Identifier}", sw.Elapsed);
        }

        var numericId = idMatch.Groups[1].Value;

        // フォールバックチェーン: MissAV → bestjavporn → javdock → 123AV
        var sites = new Func<string, List<MetadataCandidate>, MetadataProviderContext, Task<bool>>[]
        {
            TryFetchMissAv,
            TryFetchBestJavPorn,
            TryFetchJavDock,
            TryFetch123Av,
        };

        foreach (var tryFetch in sites)
        {
            var candidates = new List<MetadataCandidate>();
            try
            {
                var success = await tryFetch(numericId, candidates, context);
                if (success && candidates.Any(c => c.FieldName == "Title"))
                {
                    sw.Stop();
                    return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Fc2Alt] Site error for {Id}: {Msg}", numericId, ex.Message);
            }
        }

        sw.Stop();
        return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
            $"No metadata found on any alt site for {context.Identifier}", sw.Elapsed);
    }

    // ─── MissAV ──────────────────────────────────────────────────────────────

    private async Task<bool> TryFetchMissAv(string numericId, List<MetadataCandidate> candidates, MetadataProviderContext ctx)
    {
        var url = $"https://missav.ws/ja/fc2-ppv-{numericId}";
        _logger.LogInformation("[Fc2Alt] MissAV: {Url}", url);

        var html = await GetHtmlAsync(url, ctx);
        if (html == null) return false;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        const string src = "MissAV";
        const int prio = 55;

        // Cover from og:image
        var ogImage = MetaContent(doc, "og:image");
        if (!string.IsNullOrWhiteSpace(ogImage))
            candidates.Add(new MetadataCandidate(ProviderId, "PortraitCover", ogImage, 65, prio, Evidence: src, SourceUrl: url));

        // Title from og:title: strip "FC2-PPV-NNNNNNN - " prefix
        var rawTitle = MetaContent(doc, "og:title");
        if (!string.IsNullOrWhiteSpace(rawTitle))
        {
            var title = StripIdentifierPrefix(rawTitle!);
            if (!string.IsNullOrWhiteSpace(title))
                candidates.Add(new MetadataCandidate(ProviderId, "Title", title, 72, prio, Evidence: src, SourceUrl: url));
        }

        // Release date from og:video:release_date or meta[name=date]
        var dateStr = MetaContent(doc, "og:video:release_date")
                   ?? MetaContent(doc, "date")
                   ?? doc.DocumentNode.SelectSingleNode("//meta[@name='publish_date']")?.GetAttributeValue("content", null);
        if (!string.IsNullOrWhiteSpace(dateStr))
            candidates.Add(new MetadataCandidate(ProviderId, "ReleaseDate", NormalizeDate(dateStr!), 68, prio, Evidence: src, SourceUrl: url));

        // Genres
        var genreNodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'/genres/')]");
        if (genreNodes?.Count > 0)
        {
            var genres = genreNodes.Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim())).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            if (genres.Count > 0)
                candidates.Add(new MetadataCandidate(ProviderId, "Genre", string.Join("|", genres), 65, prio, Evidence: src, SourceUrl: url));
        }

        // Runtime from og:video:duration (seconds)
        var durationSec = MetaContent(doc, "og:video:duration");
        if (int.TryParse(durationSec, out var secs) && secs > 0)
            candidates.Add(new MetadataCandidate(ProviderId, "Runtime", (secs / 60).ToString(), 65, prio, Evidence: src, SourceUrl: url));

        _logger.LogInformation("[Fc2Alt] MissAV OK | Fields={F}", string.Join(",", candidates.Select(c => c.FieldName).Distinct()));
        return candidates.Any(c => c.FieldName == "Title");
    }

    // ─── bestjavporn (LD+JSON) ────────────────────────────────────────────────

    private async Task<bool> TryFetchBestJavPorn(string numericId, List<MetadataCandidate> candidates, MetadataProviderContext ctx)
        => await TryFetchLdJson($"https://www.bestjavporn.com/ja/video/fc2-ppv-{numericId}/", numericId, candidates, ctx, "bestjavporn");

    // ─── javdock (LD+JSON — same backend as bestjavporn) ─────────────────────

    private async Task<bool> TryFetchJavDock(string numericId, List<MetadataCandidate> candidates, MetadataProviderContext ctx)
        => await TryFetchLdJson($"https://www.javdock.com/ja/video/fc2-ppv-{numericId}/", numericId, candidates, ctx, "javdock");

    private async Task<bool> TryFetchLdJson(string url, string numericId, List<MetadataCandidate> candidates, MetadataProviderContext ctx, string src)
    {
        _logger.LogInformation("[Fc2Alt] {Src}: {Url}", src, url);

        var html = await GetHtmlAsync(url, ctx);
        if (html == null) return false;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        const int prio = 55;

        // LD+JSON VideoObject
        var ldNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (ldNodes != null)
        {
            foreach (var node in ldNodes)
            {
                try
                {
                    var json = JsonDocument.Parse(node.InnerText);
                    var root = json.RootElement;

                    if (!root.TryGetProperty("@type", out var typeEl) || typeEl.GetString() != "VideoObject")
                        continue;

                    // Title
                    if (root.TryGetProperty("name", out var nameEl))
                    {
                        var title = StripIdentifierPrefix(nameEl.GetString() ?? "");
                        if (!string.IsNullOrWhiteSpace(title))
                            candidates.Add(new MetadataCandidate(ProviderId, "Title", title, 70, prio, Evidence: src, SourceUrl: url));
                    }

                    // Cover
                    if (root.TryGetProperty("thumbnailUrl", out var thumbEl))
                    {
                        var thumb = thumbEl.ValueKind == JsonValueKind.Array
                            ? thumbEl.EnumerateArray().FirstOrDefault().GetString()
                            : thumbEl.GetString();
                        if (!string.IsNullOrWhiteSpace(thumb))
                            candidates.Add(new MetadataCandidate(ProviderId, "PortraitCover", thumb!, 62, prio, Evidence: src, SourceUrl: url));
                    }

                    // Release date
                    if (root.TryGetProperty("datePublished", out var dateEl))
                        candidates.Add(new MetadataCandidate(ProviderId, "ReleaseDate", NormalizeDate(dateEl.GetString() ?? ""), 65, prio, Evidence: src, SourceUrl: url));

                    // Duration (ISO 8601: PT1H23M45S → minutes)
                    if (root.TryGetProperty("duration", out var durEl))
                    {
                        var mins = ParseIsoDuration(durEl.GetString() ?? "");
                        if (mins > 0)
                            candidates.Add(new MetadataCandidate(ProviderId, "Runtime", mins.ToString(), 65, prio, Evidence: src, SourceUrl: url));
                    }

                    // Genres (string or array)
                    if (root.TryGetProperty("genre", out var genreEl))
                    {
                        var genres = new List<string>();
                        if (genreEl.ValueKind == JsonValueKind.Array)
                            genres.AddRange(genreEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)));
                        else if (genreEl.ValueKind == JsonValueKind.String)
                            genres.Add(genreEl.GetString()!);
                        if (genres.Count > 0)
                            candidates.Add(new MetadataCandidate(ProviderId, "Genre", string.Join("|", genres), 65, prio, Evidence: src, SourceUrl: url));
                    }

                    break;
                }
                catch (JsonException) { /* malformed script block */ }
            }
        }

        // Fallback to og:image if no cover from LD+JSON
        if (!candidates.Any(c => c.FieldName == "PortraitCover"))
        {
            var ogImage = MetaContent(doc, "og:image");
            if (!string.IsNullOrWhiteSpace(ogImage))
                candidates.Add(new MetadataCandidate(ProviderId, "PortraitCover", ogImage!, 58, prio, Evidence: src, SourceUrl: url));
        }

        _logger.LogInformation("[Fc2Alt] {Src} OK | Fields={F}", src, string.Join(",", candidates.Select(c => c.FieldName).Distinct()));
        return candidates.Any(c => c.FieldName == "Title");
    }

    // ─── 123AV ───────────────────────────────────────────────────────────────

    private async Task<bool> TryFetch123Av(string numericId, List<MetadataCandidate> candidates, MetadataProviderContext ctx)
    {
        var url = $"https://123av.com/ja/v/fc2-ppv-{numericId}";
        _logger.LogInformation("[Fc2Alt] 123AV: {Url}", url);

        var html = await GetHtmlAsync(url, ctx);
        if (html == null) return false;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        Extract123Av(doc, url, numericId, candidates);

        _logger.LogInformation("[Fc2Alt] 123AV OK | Fields={F}", string.Join(",", candidates.Select(c => c.FieldName).Distinct()));
        return candidates.Any(c => c.FieldName == "Title");
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    private async Task<string?> GetHtmlAsync(string url, MetadataProviderContext ctx)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", BrowserUserAgent[0]);
        req.Headers.Add("Accept-Language", "ja,en;q=0.9");

        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.SendAsync(req, ctx.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Fc2Alt] Network error for {Url}: {Msg}", url, ex.Message);
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Fc2Alt] HTTP {Status} for {Url}", resp.StatusCode, url);
            return null;
        }

        return await resp.Content.ReadAsStringAsync(ctx.CancellationToken);
    }

    private static string? MetaContent(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
                ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
        return node?.GetAttributeValue("content", null);
    }

    private static string StripIdentifierPrefix(string text)
    {
        // "FC2-PPV-4823194 — タイトル" or "FC2-PPV-4823194 - タイトル" or "FC2-PPV-4823194 タイトル"
        var result = Regex.Replace(text, @"^FC2-(?:PPV-)?\d+\s*[—\-–]+\s*", "", RegexOptions.IgnoreCase).Trim();
        result = Regex.Replace(result, @"^FC2-(?:PPV-)?\d+\s+", "", RegexOptions.IgnoreCase).Trim();
        return result;
    }

    private static string NormalizeDate(string raw)
    {
        // Already YYYY-MM-DD
        if (Regex.IsMatch(raw, @"^\d{4}-\d{2}-\d{2}")) return raw[..10];
        // YYYYMMDD
        var m = Regex.Match(raw, @"(\d{4})(\d{2})(\d{2})");
        if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
        return raw;
    }

    private static int ParseIsoDuration(string iso)
    {
        // PT1H23M45S or PT83M or PT5045S
        var h = Regex.Match(iso, @"(\d+)H");
        var m = Regex.Match(iso, @"(\d+)M");
        var s = Regex.Match(iso, @"(\d+)S");
        var totalSec = (h.Success ? int.Parse(h.Groups[1].Value) * 3600 : 0)
                     + (m.Success ? int.Parse(m.Groups[1].Value) * 60 : 0)
                     + (s.Success ? int.Parse(s.Groups[1].Value) : 0);
        return totalSec / 60;
    }

    internal static void Extract123Av(HtmlDocument doc, string sourceUrl, string numericId, List<MetadataCandidate> candidates)
    {
        const string provider = "Fc2Alt";
        const int priority = 55;

        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                         ?.GetAttributeValue("content", "");
        if (!string.IsNullOrWhiteSpace(ogImage))
            candidates.Add(new MetadataCandidate(provider, "PortraitCover", ogImage!, 60, priority, SourceUrl: sourceUrl));

        var h1Text = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
                  ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "")?.Trim();

        if (!string.IsNullOrWhiteSpace(h1Text))
        {
            var title = Regex.Replace(h1Text!, @"^FC2-(?:PPV-)?\d+\s*[—\-–]+\s*", "", RegexOptions.IgnoreCase).Trim();
            title = Regex.Replace(title, @"^FC2-(?:PPV-)?\d+\s+", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(title))
                candidates.Add(new MetadataCandidate(provider, "Title", title, 70, priority, SourceUrl: sourceUrl));
        }

        var genreNodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'/genres/')]");
        if (genreNodes?.Count > 0)
        {
            var genres = genreNodes.Select(n => System.Net.WebUtility.HtmlDecode(n.InnerText.Trim()))
                                   .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            if (genres.Count > 0)
                candidates.Add(new MetadataCandidate(provider, "Genre", string.Join("|", genres), 65, priority, SourceUrl: sourceUrl));
        }

        var tagNodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'/tags/')]");
        if (tagNodes?.Count > 0 && !candidates.Any(c => c.FieldName == "Genre"))
        {
            var tags = tagNodes.Select(n => System.Net.WebUtility.HtmlDecode(n.InnerText.Trim()))
                               .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            if (tags.Count > 0)
                candidates.Add(new MetadataCandidate(provider, "Genre", string.Join("|", tags), 60, priority, SourceUrl: sourceUrl));
        }

        var makerNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/makers/')]");
        if (makerNode != null)
        {
            var maker = System.Net.WebUtility.HtmlDecode(makerNode.InnerText.Trim());
            if (!string.IsNullOrWhiteSpace(maker) && !maker.Equals("FC2", StringComparison.OrdinalIgnoreCase))
                candidates.Add(new MetadataCandidate(provider, "Maker", maker, 60, priority, SourceUrl: sourceUrl));
        }

        var bodyText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? "";
        var dateMatch = Regex.Match(bodyText, @"発売日[^\d]*(\d{4}-\d{2}-\d{2})");
        if (dateMatch.Success)
            candidates.Add(new MetadataCandidate(provider, "ReleaseDate", dateMatch.Groups[1].Value, 65, priority, SourceUrl: sourceUrl));

        var runtimeMatch = Regex.Match(bodyText, @"再生時間[^\d]*(\d+):(\d{2})(?::(\d{2}))?");
        if (runtimeMatch.Success)
        {
            var h = int.Parse(runtimeMatch.Groups[1].Value);
            var min = int.Parse(runtimeMatch.Groups[2].Value);
            var totalMin = h * 60 + min;
            if (totalMin > 0)
                candidates.Add(new MetadataCandidate(provider, "Runtime", totalMin.ToString(), 65, priority, SourceUrl: sourceUrl));
        }
    }
}
