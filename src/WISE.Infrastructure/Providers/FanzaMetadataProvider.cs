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
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Cookies;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// DMM/FANZAからメタデータを取得するProvider。
/// Primary: Playwright (video.dmm.co.jp — JSレンダリング後のDOM)
/// Fallback: HtmlAgilityPack (www.dmm.co.jp — SPA shellからカバー画像のみ)
/// </summary>
public class FanzaMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ICookieProvider _cookieProvider;
    private readonly ILogger<FanzaMetadataProvider> _logger;

    // Playwright browser installation is a one-time operation per process lifetime.
    private static readonly Lazy<bool> _playwrightReady = new(() =>
    {
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public FanzaMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ICookieProvider cookieProvider,
        ILogger<FanzaMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("Fanza") ?? new MetadataProviderOptions { Priority = 80 };
        _cookieProvider = cookieProvider;
        _logger = logger;
    }

    public string ProviderId => "Fanza";
    public int Priority => _options.Priority;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video, MediaType.PhotoBook };

    // FANZAはFC2作品を取り扱わない。ConvertToCidは変換に失敗した識別子も
    // "-"除去+小文字化でCIDらしき文字列（例: "fc2ppv4409072"）を機械的に生成してしまうため、
    // 除外しないとFC2識別子が無関係な商品ページに誤マッチする恐れがある。
    public bool CanHandle(string identifier) =>
        !identifier.StartsWith("FC2", StringComparison.OrdinalIgnoreCase);

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        // FANZA同人 (d_XXXXXX) はDC/Doujinエンドポイントへルーティング
        if (Regex.IsMatch(context.Identifier, @"^d_\d+$", RegexOptions.IgnoreCase))
            return await TryFanzaDoujinAsync(context.Identifier.ToLower(), context, sw);

        try
        {
            string baseCid = ConvertToCid(context.Identifier);
            // 一部シリーズ (FTAV など) は CID 先頭に "1" が付く規則 (例: ftav00016 → 1ftav00016)
            var cidVariants = new[] { baseCid, "1" + baseCid };

            // --- Primary: Playwright on video.dmm.co.jp (CIDバリアント順に試す) ---
            foreach (var cid in cidVariants)
            {
                _logger.LogInformation("[Fanza] Identifier={Id} → CID={Cid}", context.Identifier, cid);
                var playwrightResult = await TryPlaywrightAsync(cid, context, sw);
                if (playwrightResult != null)
                {
                    sw.Stop();
                    return playwrightResult;
                }
            }

            // --- Fallback: HtmlAgilityPack on www.dmm.co.jp ---
            _logger.LogInformation("[Fanza] Playwright unavailable or incomplete for all CID variants, falling back to HtmlAgilityPack");
            return await TryHtmlAgilityPackAsync(baseCid, context, sw);
        }
        catch (TaskCanceledException ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[Fanza] Timeout after {Elapsed}ms", sw.ElapsedMilliseconds);
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, "Request timed out", sw.Elapsed, exception: ex);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[Fanza] Network error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[Fanza] Unexpected error: {Message}", ex.Message);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    // -------------------------------------------------------------------------
    // Playwright (Primary)
    // -------------------------------------------------------------------------

    private async Task<MetadataResult?> TryPlaywrightAsync(string cid, MetadataProviderContext context, Stopwatch sw)
    {
        try
        {
            _ = _playwrightReady.Value; // ensure browser installed

            var url = $"https://video.dmm.co.jp/av/content/?id={cid}";
            var policy = _cookieProvider.GetPolicy(ProviderId);
            var cookieDict = policy?.GetCookies() ?? new Dictionary<string, string>();

            _logger.LogInformation("[Playwright] Launching Chromium, navigating to {Url}", url);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
            });
            await using var browserContext = await browser.NewContextAsync(new()
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                Locale = "ja-JP",
            });

            // Inject authentication cookies
            if (cookieDict.Count > 0)
            {
                var playwrightCookies = cookieDict.Select(kv => new Microsoft.Playwright.Cookie
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    Domain = ".dmm.co.jp",
                    Path = "/",
                    SameSite = SameSiteAttribute.None,
                    Secure = true,
                }).ToList();
                await browserContext.AddCookiesAsync(playwrightCookies);
                _logger.LogInformation("[Playwright] Injected {Count} cookies", playwrightCookies.Count);
            }

            var page = await browserContext.NewPageAsync();
            string? renderedHtml = null;

            try
            {
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

                // Wait for content to render (h1 or specific product element)
                try
                {
                    await page.WaitForSelectorAsync("h1", new() { Timeout = 15000, State = WaitForSelectorState.Visible });
                }
                catch
                {
                    _logger.LogWarning("[Playwright] h1 not visible after 15s, proceeding with available DOM");
                }

                // Additional short wait for any async data fetches
                await Task.Delay(2000, context.CancellationToken);

                renderedHtml = await page.ContentAsync();
                _logger.LogInformation("[Playwright] Rendered HTML={Len}chars", renderedHtml.Length);

                // Age check detection
                if (renderedHtml.Contains("年齢認証") || renderedHtml.Contains("age_check"))
                {
                    _logger.LogWarning("[Playwright] Age check page detected. INT_SESID may be expired.");
                    return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                        "Age verification required. Refresh INT_SESID cookie.", sw.Elapsed);
                }

                // Extract all metadata via JavaScript evaluation
                var extracted = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
                    const result = {};

                    // __NEXT_DATA__ for SSR hydration data
                    const nextDataEl = document.getElementById('__NEXT_DATA__');
                    result.nextDataJson = nextDataEl ? nextDataEl.textContent : null;

                    // h1 title
                    const h1 = document.querySelector('h1');
                    result.h1Title = h1 ? h1.innerText.trim() : null;

                    // page title tag (contains product name on video.dmm.co.jp)
                    result.pageTitle = document.title;

                    // meta og:title
                    const ogTitle = document.querySelector('meta[property=""og:title""]');
                    result.ogTitle = ogTitle ? ogTitle.getAttribute('content') : null;

                    // dt/dd pairs (product details)
                    result.dtPairs = {};
                    document.querySelectorAll('dt').forEach(dt => {
                        const label = dt.innerText.trim();
                        const dd = dt.nextElementSibling;
                        if (dd && label) result.dtPairs[label] = dd.innerText.trim();
                    });

                    // th/td pairs (table layout)
                    result.tablePairs = {};
                    document.querySelectorAll('tr').forEach(tr => {
                        const th = tr.querySelector('th');
                        const td = tr.querySelector('td:last-child');
                        if (th && td) result.tablePairs[th.innerText.trim()] = td.innerText.trim();
                    });

                    // body text for pattern matching fallback
                    result.bodyText = document.body ? document.body.innerText.substring(0, 6000) : '';

                    // Sample images — collect unique image URLs containing 'jp-' (FANZA sample pattern)
                    const sampleImgs = new Set();
                    document.querySelectorAll('img[src*=""jp-""], a[href*=""jp-""]').forEach(el => {
                        const src = el.src || el.href || '';
                        if (src && src.includes('jp-')) sampleImgs.add(src);
                    });
                    result.sampleImageUrls = Array.from(sampleImgs);

                    return result;
                }");

                var candidates = new List<MetadataCandidate>();

                // Try __NEXT_DATA__ first
                if (extracted.TryGetProperty("nextDataJson", out var ndEl) && ndEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var ndJson = ndEl.GetString();
                    if (!string.IsNullOrEmpty(ndJson))
                    {
                        _logger.LogInformation("[Playwright] __NEXT_DATA__ found ({Len}chars), parsing", ndJson.Length);
                        TryParseNextDataJson(ndJson, cid, candidates, url);
                    }
                }

                // Title extraction (multiple strategies)
                if (!candidates.Any(c => c.FieldName == "Title"))
                {
                    string? title = null;

                    // h1
                    if (extracted.TryGetProperty("h1Title", out var h1El) && h1El.ValueKind == System.Text.Json.JsonValueKind.String)
                        title = h1El.GetString()?.Trim();

                    // og:title fallback
                    if (string.IsNullOrWhiteSpace(title) &&
                        extracted.TryGetProperty("ogTitle", out var ogEl) && ogEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        title = ogEl.GetString()?.Trim();

                    // page title fallback
                    if (string.IsNullOrWhiteSpace(title) &&
                        extracted.TryGetProperty("pageTitle", out var ptEl) && ptEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        title = ptEl.GetString();
                        if (title?.Contains("｜") == true) title = title.Split("｜")[0].Trim();
                        if (title?.Contains("|") == true) title = title.Split("|")[0].Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        candidates.Add(new MetadataCandidate(ProviderId, "Title", title!, 95, Priority, SourceUrl: url));
                        _logger.LogInformation("[Playwright] Title={Title} (selector=h1/og:title/pageTitle)", title);
                    }
                }

                // Extract dt/dd and table pairs
                ExtractFromPairs(extracted, "dtPairs", candidates, url);
                ExtractFromPairs(extracted, "tablePairs", candidates, url);

                // Body text fallback for fields still missing
                if (extracted.TryGetProperty("bodyText", out var btEl) && btEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var bodyText = btEl.GetString() ?? "";
                    _logger.LogInformation("[Playwright] Body text sample:\n{Text}", bodyText.Length > 600 ? bodyText[..600] : bodyText);
                    ExtractFromBodyText(bodyText, candidates, url);
                }

                // Always add cover images (CID-based URL pattern is reliable)
                AddCoverImages(candidates, cid, context.Identifier, url);

                // Sample images from page
                if (extracted.TryGetProperty("sampleImageUrls", out var siEl) && siEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var siUrl in siEl.EnumerateArray())
                    {
                        var siStr = siUrl.GetString();
                        if (!string.IsNullOrWhiteSpace(siStr))
                            candidates.Add(new MetadataCandidate(ProviderId, "SampleImage", siStr, 80, Priority, SourceUrl: url));
                    }
                    _logger.LogInformation("[Playwright] SampleImages={Count}", siEl.GetArrayLength());
                }

                _logger.LogInformation("[Playwright] Extracted {Count} candidates: {Fields}",
                    candidates.Count, string.Join(", ", candidates.Select(c => c.FieldName)));

                // Only return Playwright result if we have a meaningful title
                bool hasTitle = candidates.Any(c => c.FieldName == "Title");
                if (!hasTitle)
                {
                    _logger.LogWarning("[Playwright] Title not found — will not return Playwright result (HAP fallback)");
                    return null;
                }

                // 404/エラーページ検出: Title が "404" や "Not Found" を含む場合は正規のコンテンツページではない
                var titleCandidate = candidates.First(c => c.FieldName == "Title");
                if (titleCandidate.Value.Contains("404") || titleCandidate.Value.Contains("Not Found")
                    || titleCandidate.Value.Contains("見つかりません") || titleCandidate.Value.Contains("ページが存在しません"))
                {
                    _logger.LogInformation("[Playwright] Title '{Title}' indicates error page for CID={Cid} — falling back to HAP",
                        titleCandidate.Value.Replace('\n', ' '), cid);
                    return null;
                }

                return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Playwright] Failed with exception: {Message}", ex.Message);
            return null;
        }
    }

    private void TryParseNextDataJson(string json, string cid, List<MetadataCandidate> candidates, string url)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try common Next.js data paths
            string? title = null;
            if (TryGetNestedElement(root, out var pageProps, "props", "pageProps"))
            {
                System.Text.Json.JsonElement content = default;
                bool found = TryGetNestedElement(pageProps, out content, "content")
                          || TryGetNestedElement(pageProps, out content, "item")
                          || TryGetNestedElement(pageProps, out content, "product")
                          || TryGetNestedElement(pageProps, out content, "detail");
                if (!found) content = pageProps;

                title = GetStringProp(content, "title", "name", "productTitle");
            }

            if (string.IsNullOrWhiteSpace(title))
                title = FindStringInJson(root, "title");

            if (!string.IsNullOrWhiteSpace(title))
            {
                candidates.Add(new MetadataCandidate(ProviderId, "Title", title!, 95, Priority, SourceUrl: url));
                _logger.LogInformation("[Playwright/__NEXT_DATA__] Title={Title}", title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Playwright/__NEXT_DATA__] Parse failed");
        }
    }

    private void ExtractFromPairs(System.Text.Json.JsonElement extracted, string propName,
        List<MetadataCandidate> candidates, string url)
    {
        if (!extracted.TryGetProperty(propName, out var pairs) ||
            pairs.ValueKind != System.Text.Json.JsonValueKind.Object) return;

        var genreList = new List<string>();

        foreach (var pair in pairs.EnumerateObject())
        {
            var key = pair.Name;
            var value = pair.Value.GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value) || value == "----") continue;

            if ((key.Contains("出演者") || key.Contains("女優")) && !candidates.Any(c => c.FieldName == "Actress"))
            {
                foreach (var name in value.Split(new[] { '\n', '、', '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var n = name.Trim();
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        candidates.Add(new MetadataCandidate(ProviderId, "Actress", n, 95, Priority, SourceUrl: url));
                        _logger.LogInformation("[Playwright/{Prop}] Actress={Name}", propName, n);
                    }
                }
            }
            else if (key.Contains("メーカー") && !candidates.Any(c => c.FieldName == "Maker"))
            {
                candidates.Add(new MetadataCandidate(ProviderId, "Maker", value, 95, Priority, SourceUrl: url));
                _logger.LogInformation("[Playwright/{Prop}] Maker={V}", propName, value);
            }
            else if (key.Contains("レーベル") && !candidates.Any(c => c.FieldName == "Label"))
            {
                candidates.Add(new MetadataCandidate(ProviderId, "Label", value, 90, Priority, SourceUrl: url));
                _logger.LogInformation("[Playwright/{Prop}] Label={V}", propName, value);
            }
            else if (key.Contains("シリーズ") && !candidates.Any(c => c.FieldName == "Series"))
            {
                candidates.Add(new MetadataCandidate(ProviderId, "Series", value, 85, Priority, SourceUrl: url));
                _logger.LogInformation("[Playwright/{Prop}] Series={V}", propName, value);
            }
            else if ((key.Contains("発売日") || key.Contains("配信開始日")) && !candidates.Any(c => c.FieldName == "ReleaseDate"))
            {
                var dateMatch = Regex.Match(value, @"\d{4}[/\-]\d{2}[/\-]\d{2}");
                if (dateMatch.Success)
                {
                    candidates.Add(new MetadataCandidate(ProviderId, "ReleaseDate", dateMatch.Value, 90, Priority, SourceUrl: url));
                    _logger.LogInformation("[Playwright/{Prop}] ReleaseDate={V}", propName, dateMatch.Value);
                }
            }
            else if (key.Contains("収録時間") && !candidates.Any(c => c.FieldName == "Runtime"))
            {
                var minMatch = Regex.Match(value, @"(\d+)");
                if (minMatch.Success)
                {
                    candidates.Add(new MetadataCandidate(ProviderId, "Runtime", minMatch.Value, 90, Priority, SourceUrl: url));
                    _logger.LogInformation("[Playwright/{Prop}] Runtime={V}", propName, minMatch.Value);
                }
            }
            else if (key.Contains("ジャンル"))
            {
                foreach (var genre in value.Split(new[] { '\n', '、', '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var g = genre.Trim();
                    if (!string.IsNullOrWhiteSpace(g) && !genreList.Contains(g))
                        genreList.Add(g);
                }
            }
        }

        // Store all genres as a single |-joined candidate to avoid FieldName+ProviderId dedup
        if (genreList.Count > 0 && !candidates.Any(c => c.FieldName == "Genre"))
        {
            var joined = string.Join("|", genreList);
            candidates.Add(new MetadataCandidate(ProviderId, "Genre", joined, 80, Priority, SourceUrl: url));
            _logger.LogInformation("[Playwright/{Prop}] Genre={V}", propName, joined);
        }
    }

    private void ExtractFromBodyText(string bodyText, List<MetadataCandidate> candidates, string url)
    {
        // Pattern-match Japanese field labels from body text as last resort
        var patterns = new[]
        {
            (Field: "Actress", Pattern: @"出演者\s*[：:]\s*([^\n]+)"),
            (Field: "Maker",   Pattern: @"メーカー\s*[：:]\s*([^\n]+)"),
            (Field: "Label",   Pattern: @"レーベル\s*[：:]\s*([^\n]+)"),
            (Field: "Series",  Pattern: @"シリーズ\s*[：:]\s*([^\n]+)"),
            (Field: "ReleaseDate", Pattern: @"(?:発売日|配信開始日)\s*[：:]\s*(\d{4}[/\-]\d{2}[/\-]\d{2})"),
            (Field: "Runtime", Pattern: @"収録時間\s*[：:]\s*(\d+)"),
        };

        foreach (var (field, pattern) in patterns)
        {
            if (candidates.Any(c => c.FieldName == field)) continue;
            var m = Regex.Match(bodyText, pattern);
            if (m.Success)
            {
                var val = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val) && val != "----")
                {
                    candidates.Add(new MetadataCandidate(ProviderId, field, val, 80, Priority, SourceUrl: url));
                    _logger.LogInformation("[Playwright/BodyText] {Field}={Val}", field, val);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // HtmlAgilityPack (Fallback)
    // -------------------------------------------------------------------------

    private async Task<MetadataResult> TryHtmlAgilityPackAsync(string cid, MetadataProviderContext context, Stopwatch sw)
    {
        var results = new List<MetadataCandidate>();
        var policy = _cookieProvider.GetPolicy(ProviderId);
        string cookieHeader = policy?.GetCookieHeader() ?? string.Empty;

        string detailUrl = $"https://www.dmm.co.jp/digital/videoa/-/detail/=/cid={cid}/";
        _logger.LogInformation("[HAP] Fetching {Url}", detailUrl);

        var response = await SendRequestAsync(detailUrl, cookieHeader, context);
        var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

        _logger.LogInformation("[HAP] HTTP {Status} | Elapsed={Elapsed}ms | HTML={Len}chars",
            response.StatusCode, sw.ElapsedMilliseconds, html.Length);

        bool isAgeCheck = html.Contains("age_check/_next") || html.Contains("age_check/images")
            || html.Contains("年齢認証") || html.Contains("declared=yes");
        bool isNotFound = html.Contains("該当する商品が見つかりません") || html.Contains("お探しの商品は見つかりませんでした")
            || response.StatusCode == System.Net.HttpStatusCode.NotFound;

        if (isNotFound)
        {
            // 1. 1-prefix CID バリアントを試す (FTAV など: ftav00016 → 1ftav00016)
            var altCid = "1" + cid;
            var altUrl = $"https://www.dmm.co.jp/digital/videoa/-/detail/=/cid={altCid}/";
            _logger.LogInformation("[HAP] digital/videoa 404 → trying 1-prefix variant: {Url}", altUrl);
            var altResponse = await SendRequestAsync(altUrl, cookieHeader, context);
            var altHtml = await altResponse.Content.ReadAsStringAsync(context.CancellationToken);
            bool altNotFound = altHtml.Contains("該当する商品が見つかりません") || altHtml.Contains("お探しの商品は見つかりませんでした")
                || altResponse.StatusCode == System.Net.HttpStatusCode.NotFound;
            bool altAgeCheck = altHtml.Contains("age_check/_next") || altHtml.Contains("年齢認証");
            if (!altNotFound && !altAgeCheck && altResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("[HAP] 1-prefix variant hit: {Url}", altUrl);
                return ParseAndReturnHap(altHtml, altUrl, altCid, context, results, sw);
            }

            // 2. mono/dvd (通販パッケージ版) を試す
            var monoCid = ConvertToMonoCid(context.Identifier);
            var monoUrl = $"https://www.dmm.co.jp/mono/dvd/-/detail/=/cid={monoCid}/";
            _logger.LogInformation("[HAP] 1-prefix also 404 → trying mono/dvd: {Url}", monoUrl);
            var monoResponse = await SendRequestAsync(monoUrl, cookieHeader, context);
            var monoHtml = await monoResponse.Content.ReadAsStringAsync(context.CancellationToken);
            bool monoNotFound = monoHtml.Contains("該当する商品が見つかりません")
                || monoHtml.Contains("お探しの商品は見つかりませんでした")
                || monoResponse.StatusCode == System.Net.HttpStatusCode.NotFound;
            if (!monoNotFound && monoResponse.IsSuccessStatusCode)
            {
                bool monoAgeCheck = monoHtml.Contains("age_check/_next") || monoHtml.Contains("年齢認証");
                if (monoAgeCheck)
                    return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification, "Age verification required.", sw.Elapsed);
                return ParseAndReturnHap(monoHtml, monoUrl, cid, context, results, sw);
            }
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound, $"Product not found: {cid} (digital + 1-prefix + mono)", sw.Elapsed);
        }

        if (isAgeCheck)
            return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                "Age verification required. Refresh INT_SESID.", sw.Elapsed);

        bool isSpaShell = html.Contains("assets.video.dmm.co.jp/_next/")
            && !html.Contains("<h1")
            && !html.Contains("__NEXT_DATA__");

        if (isSpaShell)
        {
            _logger.LogWarning("[HAP] SPA shell detected for CID={Cid}. Returning cover images only.", cid);
            AddCoverImages(results, cid, context.Identifier, detailUrl);
            return MetadataResult.Succeeded(ProviderId, results, sw.Elapsed);
        }

        return ParseAndReturnHap(html, detailUrl, cid, context, results, sw);
    }

    private MetadataResult ParseAndReturnHap(string html, string detailUrl, string cid,
        MetadataProviderContext context, List<MetadataCandidate> results, Stopwatch sw)
    {
        // Full HTML parse (SSR page)
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@id='title']")
            ?? doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'title')]")
            ?? doc.DocumentNode.SelectSingleNode("//h1");

        if (titleNode == null)
            return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "Title element not found", sw.Elapsed);

        string title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());
        if (title.Contains("｜")) title = title.Split("｜")[0].Trim();
        if (string.IsNullOrWhiteSpace(title))
            return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "Title is empty", sw.Elapsed);

        results.Add(new MetadataCandidate(ProviderId, "Title", title, 90, Priority, SourceUrl: detailUrl));
        AddCoverImages(results, cid, context.Identifier, detailUrl);
        ParseDetailTable(doc, results, detailUrl);

        return MetadataResult.Succeeded(ProviderId, results, sw.Elapsed);
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static string ConvertToCid(string identifier)
    {
        if (Regex.IsMatch(identifier, @"^[a-z][a-z0-9]+\d{5}$"))
            return identifier;

        var match = Regex.Match(identifier, @"^([A-Za-z]+)-(\d+)$");
        if (match.Success)
        {
            string prefix = match.Groups[1].Value.ToLower();
            string num = match.Groups[2].Value;
            return $"{prefix}{num.PadLeft(5, '0')}";
        }

        return identifier.Replace("-", "").ToLower();
    }

    /// <summary>
    /// カバー画像URLを候補として追加する。
    /// 信頼度の高い順に: mono/dvd pb(高解像) > mono/dvd ps > digital pb > digital ps
    /// FetchMetadataJobUseCase がダウンロード時に上位から試し、最小サイズを満たす最初のURLを採用する。
    /// </summary>
    private void AddCoverImages(List<MetadataCandidate> results, string cid, string identifier, string sourceUrl)
    {
        var monoCid = ConvertToMonoCid(identifier);

        // Portrait (縦長パッケージ) — 高解像から低解像の順
        var portraitCandidates = new[]
        {
            ($"https://pics.dmm.co.jp/mono/movie/adult/{monoCid}/{monoCid}pb.jpg",   97),
            ($"https://pics.dmm.co.jp/mono/movie/adult/{monoCid}/{monoCid}ps.jpg",   93),
            ($"https://awsimgsrc.dmm.co.jp/pics_dig/digital/video/{cid}/{cid}pb.jpg", 91),
            ($"https://awsimgsrc.dmm.co.jp/pics_dig/digital/video/{cid}/{cid}ps.jpg", 89),
        };
        foreach (var (url, conf) in portraitCandidates)
            results.Add(new MetadataCandidate(ProviderId, "PortraitCover", url, conf, Priority, SourceUrl: sourceUrl));

        // Landscape (横長パッケージ)
        var landscapeCandidates = new[]
        {
            ($"https://pics.dmm.co.jp/mono/movie/adult/{monoCid}/{monoCid}pl.jpg",   96),
            ($"https://awsimgsrc.dmm.co.jp/pics_dig/digital/video/{cid}/{cid}pl.jpg", 90),
        };
        foreach (var (url, conf) in landscapeCandidates)
            results.Add(new MetadataCandidate(ProviderId, "LandscapeCover", url, conf, Priority, SourceUrl: sourceUrl));

        _logger.LogInformation("[Fanza] Added {Count} cover candidates (monoCid={MonoCid}, cid={Cid})",
            portraitCandidates.Length + landscapeCandidates.Length, monoCid, cid);
    }

    /// <summary>
    /// FANZA通販（mono/dvd）向けCID変換。ゼロパディングなし。
    /// IPX-398 → ipx398, FTAV-015 → ftav15, EKDV-775 → ekdv775
    /// </summary>
    private static string ConvertToMonoCid(string identifier)
    {
        var match = Regex.Match(identifier, @"^([A-Za-z]+)-?(\d+)$");
        if (match.Success)
        {
            var prefix = match.Groups[1].Value.ToLower();
            var num = int.Parse(match.Groups[2].Value).ToString(); // 先頭ゼロ除去
            return $"{prefix}{num}";
        }
        return identifier.Replace("-", "").ToLower();
    }

    private void ParseDetailTable(HtmlDocument doc, List<MetadataCandidate> results, string sourceUrl)
    {
        var tableRows = doc.DocumentNode.SelectNodes("//table[@class='mg-b20']//tr")
            ?? doc.DocumentNode.SelectNodes("//table//tr");

        if (tableRows == null) return;

        foreach (var row in tableRows)
        {
            var headerNode = row.SelectSingleNode("td[1]") ?? row.SelectSingleNode("th[1]");
            var dataNode = row.SelectSingleNode("td[2]") ?? row.SelectSingleNode("td[last()]");
            if (headerNode == null || dataNode == null) continue;

            string header = System.Net.WebUtility.HtmlDecode(headerNode.InnerText.Trim());
            string data = System.Net.WebUtility.HtmlDecode(dataNode.InnerText.Trim());
            if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(data)) continue;

            if (header.Contains("配信開始日") || header.Contains("発売日"))
            {
                var m = Regex.Match(data, @"\d{4}/\d{2}/\d{2}");
                if (m.Success) results.Add(new MetadataCandidate(ProviderId, "ReleaseDate", m.Value, 90, Priority, SourceUrl: sourceUrl));
            }
            else if (header.Contains("収録時間"))
            {
                var m = Regex.Match(data, @"(\d+)");
                if (m.Success) results.Add(new MetadataCandidate(ProviderId, "Runtime", m.Value, 90, Priority, SourceUrl: sourceUrl));
            }
            else if (header.Contains("出演者") || header.Contains("女優"))
            {
                foreach (var a in dataNode.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                {
                    string name = System.Net.WebUtility.HtmlDecode(a.InnerText.Trim());
                    if (!string.IsNullOrWhiteSpace(name))
                        results.Add(new MetadataCandidate(ProviderId, "Actress", name, 90, Priority, SourceUrl: sourceUrl));
                }
            }
            else if (header.Contains("メーカー"))
            {
                var a = dataNode.SelectSingleNode(".//a");
                string maker = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? data).Trim());
                if (!string.IsNullOrWhiteSpace(maker))
                    results.Add(new MetadataCandidate(ProviderId, "Maker", maker, 90, Priority, SourceUrl: sourceUrl));
            }
            else if (header.Contains("シリーズ"))
            {
                var a = dataNode.SelectSingleNode(".//a");
                string series = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? data).Trim());
                if (!string.IsNullOrWhiteSpace(series) && series != "----")
                    results.Add(new MetadataCandidate(ProviderId, "Series", series, 85, Priority, SourceUrl: sourceUrl));
            }
            else if (header.Contains("レーベル"))
            {
                var a = dataNode.SelectSingleNode(".//a");
                string label = System.Net.WebUtility.HtmlDecode((a?.InnerText ?? data).Trim());
                if (!string.IsNullOrWhiteSpace(label) && label != "----")
                    results.Add(new MetadataCandidate(ProviderId, "Label", label, 85, Priority, SourceUrl: sourceUrl));
            }
            else if (header.Contains("ジャンル"))
            {
                foreach (var g in dataNode.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                {
                    string genre = System.Net.WebUtility.HtmlDecode(g.InnerText.Trim());
                    if (!string.IsNullOrWhiteSpace(genre))
                        results.Add(new MetadataCandidate(ProviderId, "Genre", genre, 80, Priority, SourceUrl: sourceUrl));
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // FANZA同人 (d_XXXXXX) ハンドラー
    // -------------------------------------------------------------------------

    private async Task<MetadataResult> TryFanzaDoujinAsync(string cid, MetadataProviderContext context, Stopwatch sw)
    {
        var policy = _cookieProvider.GetPolicy(ProviderId);
        string cookieHeader = policy?.GetCookieHeader() ?? string.Empty;

        var url = $"https://www.dmm.co.jp/dc/doujin/-/detail/=/cid={cid}/";
        _logger.LogInformation("[Fanza/Doujin] Fetching {Url}", url);

        try
        {
            var response = await SendRequestAsync(url, cookieHeader, context);
            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

            _logger.LogInformation("[Fanza/Doujin] HTTP {Status} | {Ms}ms | {Len}chars",
                response.StatusCode, sw.ElapsedMilliseconds, html.Length);

            if (!response.IsSuccessStatusCode)
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound, $"HTTP {response.StatusCode}", sw.Elapsed);

            bool isAgeCheck = html.Contains("age_check") || html.Contains("年齢認証");
            if (isAgeCheck)
                return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification, "Age verification required", sw.Elapsed);

            bool isNotFound = html.Contains("該当する商品が見つかりません") || html.Contains("お探しの商品");
            if (isNotFound)
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound, $"Product not found: {cid}", sw.Elapsed);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var candidates = new List<MetadataCandidate>();

            void Add(string field, string? value, int confidence = 85)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    candidates.Add(new MetadataCandidate(ProviderId, field, value!.Trim(), confidence, Priority, SourceUrl: url));
            }

            // Title: og:title または h1
            var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
                ?.GetAttributeValue("content", null);
            var h1Title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText;
            var title = System.Net.WebUtility.HtmlDecode(ogTitle ?? h1Title ?? "");
            if (title.Contains("｜")) title = title.Split("｜")[0].Trim();
            Add("Title", title, 90);

            // Cover: og:image → PortraitCover/LandscapeCover
            var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                ?.GetAttributeValue("content", null);
            // CID-based URL pattern for doujin (より高解像度)
            Add("LandscapeCover", $"https://pics.dmm.co.jp/digital/comic/{cid}/{cid}pl.jpg", 90);
            Add("PortraitCover", $"https://pics.dmm.co.jp/digital/comic/{cid}/{cid}pt.jpg", 88);
            if (!string.IsNullOrWhiteSpace(ogImage))
            {
                Add("LandscapeCover", ogImage, 75);
                Add("PortraitCover", ogImage, 65);
            }

            // Detail table parsing
            var tableRows = doc.DocumentNode.SelectNodes("//table//tr");
            if (tableRows != null)
            {
                foreach (var row in tableRows)
                {
                    var th = row.SelectSingleNode(".//th")?.InnerText?.Trim()
                           ?? row.SelectSingleNode(".//td[1]")?.InnerText?.Trim() ?? "";
                    var td = row.SelectSingleNode(".//td[last()]");
                    if (td == null) continue;
                    var val = System.Net.WebUtility.HtmlDecode(td.InnerText.Trim());
                    if (string.IsNullOrWhiteSpace(val) || val == "----") continue;

                    if (th.Contains("作者") || th.Contains("著者"))
                        Add("author", val);
                    else if (th.Contains("サークル"))
                        Add("circle", val);
                    else if (th.Contains("発売日") || th.Contains("配信"))
                    {
                        var m = Regex.Match(val, @"\d{4}[/\-]\d{2}[/\-]\d{2}");
                        if (m.Success) Add("ReleaseDate", m.Value);
                    }
                    else if (th.Contains("ジャンル"))
                    {
                        var genres = td.SelectNodes(".//a")
                            ?.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText.Trim()))
                            .Where(g => !string.IsNullOrWhiteSpace(g))
                            .ToList() ?? new List<string>();
                        if (genres.Count > 0) Add("Genre", string.Join("|", genres), 80);
                    }
                }
            }

            sw.Stop();
            if (!candidates.Any(c => c.FieldName == "Title"))
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "Title not found in doujin page", sw.Elapsed);

            _logger.LogInformation("[Fanza/Doujin] OK | Fields={Fields}",
                string.Join(",", candidates.Select(c => c.FieldName).Distinct()));
            return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[Fanza/Doujin] Exception for {Cid}", cid);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private Task<HttpResponseMessage> SendRequestAsync(string url, string cookieHeader, MetadataProviderContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.Add("Accept-Language", "ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7");
        // NOTE: Do NOT set Accept-Encoding manually — disables HttpClient's auto-decompression.

        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.Add("Cookie", cookieHeader);

        return _httpClient.SendAsync(request, context.CancellationToken);
    }

    private static bool TryGetNestedElement(System.Text.Json.JsonElement el, out System.Text.Json.JsonElement result, params string[] keys)
    {
        result = el;
        foreach (var key in keys)
        {
            if (result.ValueKind != System.Text.Json.JsonValueKind.Object || !result.TryGetProperty(key, out result))
                return false;
        }
        return true;
    }

    private static string? GetStringProp(System.Text.Json.JsonElement el, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.Object &&
                el.TryGetProperty(key, out var val) && val.ValueKind == System.Text.Json.JsonValueKind.String)
                return val.GetString();
        }
        return null;
    }

    private static string? FindStringInJson(System.Text.Json.JsonElement el, string key, int depth = 0)
    {
        if (depth > 8) return null;
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name == key && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return prop.Value.GetString();
                var nested = FindStringInJson(prop.Value, key, depth + 1);
                if (nested != null) return nested;
            }
        }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var nested = FindStringInJson(item, key, depth + 1);
                if (nested != null) return nested;
            }
        }
        return null;
    }
}
