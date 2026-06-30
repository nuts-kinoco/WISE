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

namespace WISE.Infrastructure.Providers;

/// <summary>
/// Fetches metadata from DLsite.com for RJ/VJ/BJ product identifiers.
/// Handles both DLsite Adult (www.dlsite.com/maniax) and All-Ages (www.dlsite.com/home).
/// Priority=80.
/// </summary>
public class DLSiteMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ILogger<DLSiteMetadataProvider> _logger;

    // RJ = 同人(Adult), BJ = 書籍, VJ = PCゲーム
    private static readonly Regex RjPattern = new(@"^RJ\d{6,}", RegexOptions.IgnoreCase);
    private static readonly Regex VjPattern = new(@"^VJ\d{6,}", RegexOptions.IgnoreCase);
    private static readonly Regex BjPattern = new(@"^BJ\d{6,}", RegexOptions.IgnoreCase);

    public string ProviderId => "DLSite";
    public int Priority => _options.Priority;

    public DLSiteMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ILogger<DLSiteMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("DLSite") ?? new MetadataProviderOptions { Priority = 80, IsEnabled = true };
        _logger = logger;
    }

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var id = context.Identifier.ToUpper();
        var url = ResolveUrl(id);
        if (url == null)
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                $"Identifier {id} is not a DLSite product ID (expected RJ/VJ/BJ prefix)", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[DLSite] Strategy=Http | URL={Url}", url);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept-Language", "ja,en;q=0.9");

            var response = await _httpClient.SendAsync(request, context.CancellationToken);
            sw.Stop();

            _logger.LogInformation("[DLSite] HTTP {Status} | {Ms}ms", response.StatusCode, sw.ElapsedMilliseconds);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return MetadataResult.Failed(ProviderId, FailureReason.RateLimit, "429 Rate Limited", sw.Elapsed);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound, $"Product {id} not found", sw.Elapsed);

            if (!response.IsSuccessStatusCode)
                return MetadataResult.Failed(ProviderId, FailureReason.Network, $"HTTP {response.StatusCode}", sw.Elapsed);

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var candidates = ExtractMetadata(doc, id, url);

            if (!candidates.Any())
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No fields extracted", sw.Elapsed);

            _logger.LogInformation("[DLSite] OK | Fields={Fields} | {Ms}ms",
                string.Join(",", candidates.Select(c => c.FieldName).Distinct()), sw.ElapsedMilliseconds);

            return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed);
        }
        catch (TaskCanceledException ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.Timeout, "Timeout", sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[DLSite] Exception for {Id}", id);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private static string? ResolveUrl(string id)
    {
        if (RjPattern.IsMatch(id)) return $"https://www.dlsite.com/maniax/work/=/product_id/{id}.html";
        if (VjPattern.IsMatch(id)) return $"https://www.dlsite.com/soft/work/=/product_id/{id}.html";
        if (BjPattern.IsMatch(id)) return $"https://www.dlsite.com/books/work/=/product_id/{id}.html";
        return null;
    }

    private List<MetadataCandidate> ExtractMetadata(HtmlDocument doc, string id, string sourceUrl)
    {
        var candidates = new List<MetadataCandidate>();

        void Add(string field, string? value, int confidence = 80)
        {
            if (!string.IsNullOrWhiteSpace(value))
                candidates.Add(new MetadataCandidate(ProviderId, field, value.Trim(), confidence, Priority, sourceUrl));
        }

        // Title: <meta property="og:title"> or <h1 id="work_name">
        var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
            ?.GetAttributeValue("content", null)
            ?? doc.DocumentNode.SelectSingleNode("//*[@id='work_name']")?.InnerText;
        Add("Title", CleanText(title), 90);

        // DLsite work_outline table — all fields in <table id="work_outline">
        var outlineTable = doc.DocumentNode.SelectSingleNode("//table[@id='work_outline']");
        if (outlineTable != null)
        {
            foreach (var row in outlineTable.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var th = row.SelectSingleNode(".//th")?.InnerText?.Trim() ?? "";
                var td = row.SelectSingleNode(".//td");
                if (td == null) continue;

                var value = CleanText(td.InnerText);
                switch (th)
                {
                    case "サークル名":
                    case "Circle":
                        Add("circle", value);
                        // Also extract link text for more precise author name
                        var circleLink = td.SelectSingleNode(".//a");
                        if (circleLink != null) Add("circle", CleanText(circleLink.InnerText));
                        break;
                    case "作者":
                    case "Author":
                        Add("author", value);
                        break;
                    case "発売日":
                    case "Release date":
                        Add("release_date", value);
                        break;
                    case "シリーズ名":
                    case "Series":
                        Add("series", value);
                        break;
                    case "ファイル形式":
                    case "File format":
                        Add("file_format", value);
                        break;
                    case "ファイル容量":
                    case "File size":
                        Add("file_size", value);
                        break;
                    case "ジャンル":
                    case "Genre":
                        foreach (var a in td.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                            Add("Genre", CleanText(a.InnerText));
                        break;
                    case "年齢指定":
                    case "Age rating":
                        Add("age_rating", value);
                        break;
                }
            }
        }

        // Description: <meta name="description"> or div#work_parts_container
        var desc = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")
            ?.GetAttributeValue("content", null);
        Add("description", CleanText(desc));

        // Cover image: og:image (DLSiteのメイン画像は通常横長)
        var cover = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
            ?.GetAttributeValue("content", null);
        Add("LandscapeCover", cover, 85);
        Add("PortraitCover", cover, 70); // 縦長が取れない場合の代替

        // DLsite product ID
        Add("dlsite_id", id, 100);

        return candidates;
    }

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return System.Net.WebUtility.HtmlDecode(
            Regex.Replace(text.Trim(), @"\s+", " "));
    }
}
