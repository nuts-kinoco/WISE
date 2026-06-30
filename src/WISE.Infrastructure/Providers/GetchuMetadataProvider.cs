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
/// Fetches metadata from Getchu.com for doujin/game products.
/// Getchu identifiers look like numeric product IDs (e.g. 1234567).
/// Priority=70.
/// </summary>
public class GetchuMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ILogger<GetchuMetadataProvider> _logger;

    private static readonly Regex GetchuIdPattern = new(@"^\d{5,7}$");

    public string ProviderId => "Getchu";
    public int Priority => _options.Priority;

    public GetchuMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ILogger<GetchuMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("Getchu") ?? new MetadataProviderOptions { Priority = 70, IsEnabled = true };
        _logger = logger;
    }

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var id = context.Identifier;
        if (!GetchuIdPattern.IsMatch(id))
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                $"Identifier '{id}' is not a Getchu product ID (expected 5-7 digits)", TimeSpan.Zero);

        var url = $"https://www.getchu.com/soft.phtml?id={id}&gc=gc";
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Getchu] Strategy=Http | URL={Url}", url);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept-Language", "ja,en;q=0.9");
            request.Headers.Add("Referer", "https://www.getchu.com/");

            var response = await _httpClient.SendAsync(request, context.CancellationToken);
            sw.Stop();

            _logger.LogInformation("[Getchu] HTTP {Status} | {Ms}ms", response.StatusCode, sw.ElapsedMilliseconds);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return MetadataResult.Failed(ProviderId, FailureReason.RateLimit, "429 Rate Limited", sw.Elapsed);

            if (!response.IsSuccessStatusCode)
                return MetadataResult.Failed(ProviderId, FailureReason.Network, $"HTTP {response.StatusCode}", sw.Elapsed);

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);

            if (html.Contains("ご指定のページは見つかりませんでした") || html.Contains("商品が見つかりません"))
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound, $"Product {id} not found on Getchu", sw.Elapsed);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var candidates = ExtractMetadata(doc, id, url);

            if (!candidates.Any())
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No fields extracted", sw.Elapsed);

            _logger.LogInformation("[Getchu] OK | Fields={Fields} | {Ms}ms",
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
            _logger.LogWarning(ex, "[Getchu] Exception for {Id}", id);
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }

    private List<MetadataCandidate> ExtractMetadata(HtmlDocument doc, string id, string sourceUrl)
    {
        var candidates = new List<MetadataCandidate>();

        void Add(string field, string? value, int confidence = 75)
        {
            if (!string.IsNullOrWhiteSpace(value))
                candidates.Add(new MetadataCandidate(ProviderId, field, value.Trim(), confidence, Priority, sourceUrl));
        }

        // Title
        var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
            ?.GetAttributeValue("content", null)
            ?? doc.DocumentNode.SelectSingleNode("//*[@id='soft-title']")?.InnerText;
        Add("Title", CleanText(title), 85);

        // Table rows in Getchu detail page (div#soft-main table)
        var tables = doc.DocumentNode.SelectNodes("//table[@class='soft-table']");
        if (tables != null)
        {
            foreach (var table in tables)
            {
                foreach (var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var th = CleanText(row.SelectSingleNode(".//th")?.InnerText) ?? "";
                    var td = row.SelectSingleNode(".//td");
                    if (td == null) continue;
                    var value = CleanText(td.InnerText);

                    switch (th)
                    {
                        case "メーカー":
                            Add("maker", value);
                            break;
                        case "発売日":
                            Add("release_date", value);
                            break;
                        case "原画":
                            Add("author", value);
                            break;
                        case "シナリオ":
                            Add("scenario_writer", value);
                            break;
                        case "ジャンル":
                            foreach (var a in td.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                                Add("Genre", CleanText(a.InnerText));
                            break;
                    }
                }
            }
        }

        // Description
        var desc = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")
            ?.GetAttributeValue("content", null);
        Add("description", CleanText(desc));

        // Cover image
        var img = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
            ?.GetAttributeValue("content", null);
        Add("cover_url", img);

        Add("getchu_id", id, 100);

        return candidates;
    }

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return System.Net.WebUtility.HtmlDecode(
            Regex.Replace(text.Trim(), @"\s+", " "));
    }
}
