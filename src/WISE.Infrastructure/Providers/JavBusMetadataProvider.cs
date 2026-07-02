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

public class JavBusMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly MetadataProviderOptions _options;
    private readonly ILogger<JavBusMetadataProvider> _logger;

    public JavBusMetadataProvider(
        HttpClient httpClient,
        IOptionsMonitor<MetadataProviderOptions> options,
        ILogger<JavBusMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Get("JavBus") ?? new MetadataProviderOptions { Priority = 60 };
        _logger = logger;
    }

    public string ProviderId => "JavBus";
    public int Priority => _options.Priority;
    public IReadOnlySet<MediaType>? SupportedMediaTypes => new HashSet<MediaType> { MediaType.Video };

    // JavBusはFC2作品を取り扱わない。検索フォールバック実装のため、除外しないと
    // FC2識別子が無関係な検索結果に弱くマッチしてしまう恐れがある。
    public bool CanHandle(string identifier) =>
        !identifier.StartsWith("FC2", StringComparison.OrdinalIgnoreCase);

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        if (!_options.IsEnabled)
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, "Provider is disabled", TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var results = new List<MetadataCandidate>();

        // JavBusは英語版を使用（文字化け回避）
        string url = $"https://www.javbus.com/en/{context.Identifier}";
        _logger.LogInformation("[JavBus] Strategy=Http | URL={Url}", url);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var response = await _httpClient.SendAsync(request, context.CancellationToken);

            _logger.LogInformation("[JavBus] HTTP {Status} | Elapsed={Elapsed}ms", response.StatusCode, sw.ElapsedMilliseconds);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.RateLimit, "429 Rate Limited", sw.Elapsed);
            }

            if (!response.IsSuccessStatusCode)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound,
                    $"HTTP {response.StatusCode} for {context.Identifier}", sw.Elapsed);
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            sw.Stop();

            // Cloudflare/Bot検出
            if (html.Contains("cf-browser-verification") || html.Contains("cloudflare"))
            {
                _logger.LogWarning("[JavBus] Cloudflare protection detected. FutureStrategy=Browser");
                return MetadataResult.Failed(ProviderId, FailureReason.AgeVerification,
                    "Cloudflare protection detected. Future: Browser fallback.", sw.Elapsed);
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Title
            var titleNode = doc.DocumentNode.SelectSingleNode("//h3");
            if (titleNode != null)
            {
                var title = titleNode.InnerText.Trim();
                results.Add(new MetadataCandidate(ProviderId, "Title", title, 80, Priority, SourceUrl: url));
                _logger.LogInformation("[JavBus] Title={Title}", title);
            }
            else
            {
                _logger.LogWarning("[JavBus] Title not found (ParserError)");
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError,
                    "Title element not found", sw.Elapsed);
            }

            // Cover
            var coverNode = doc.DocumentNode.SelectSingleNode("//a[@class='bigImage']/img");
            var coverUrl = coverNode?.GetAttributeValue("src", string.Empty);
            if (!string.IsNullOrEmpty(coverUrl))
            {
                results.Add(new MetadataCandidate(ProviderId, "PortraitCover", coverUrl, 80, Priority, SourceUrl: url));
                _logger.LogInformation("[JavBus] Cover={CoverUrl}", coverUrl);
            }

            // Info block
            var infoNodes = doc.DocumentNode.SelectNodes("//div[@class='col-md-3 info']/p");
            if (infoNodes != null)
            {
                foreach (var p in infoNodes)
                {
                    var text = p.InnerText;
                    if (text.Contains("Studio:"))
                    {
                        var maker = p.SelectSingleNode("a")?.InnerText.Trim();
                        if (!string.IsNullOrEmpty(maker))
                        {
                            results.Add(new MetadataCandidate(ProviderId, "Maker", maker, 80, Priority, SourceUrl: url));
                            _logger.LogInformation("[JavBus] Maker={Maker}", maker);
                        }
                    }
                    else if (text.Contains("Label:"))
                    {
                        var label = p.SelectSingleNode("a")?.InnerText.Trim();
                        if (!string.IsNullOrEmpty(label))
                            results.Add(new MetadataCandidate(ProviderId, "Label", label, 80, Priority, SourceUrl: url));
                    }
                    else if (text.Contains("Series:"))
                    {
                        var series = p.SelectSingleNode("a")?.InnerText.Trim();
                        if (!string.IsNullOrEmpty(series))
                            results.Add(new MetadataCandidate(ProviderId, "Series", series, 80, Priority, SourceUrl: url));
                    }
                    else if (text.Contains("Release Date:"))
                    {
                        var dateText = text.Replace("Release Date:", "").Trim();
                        if (!string.IsNullOrEmpty(dateText))
                            results.Add(new MetadataCandidate(ProviderId, "ReleaseDate", dateText, 80, Priority, SourceUrl: url));
                    }
                    else if (text.Contains("Length:"))
                    {
                        var lengthText = text.Replace("Length:", "").Replace("minutes", "").Trim();
                        if (!string.IsNullOrEmpty(lengthText))
                            results.Add(new MetadataCandidate(ProviderId, "Runtime", lengthText, 80, Priority, SourceUrl: url));
                    }
                }
            }

            // Genres
            var genreNodes = doc.DocumentNode.SelectNodes("//span[@class='genre']/label/a[not(contains(@href,'star'))]")
                          ?? doc.DocumentNode.SelectNodes("//span[@class='genre']/a[not(contains(@href,'star'))]");
            if (genreNodes != null)
                foreach (var g in genreNodes)
                {
                    var genreText = g.InnerText.Trim();
                    if (!string.IsNullOrEmpty(genreText))
                        results.Add(new MetadataCandidate(ProviderId, "Genre", genreText, 80, Priority, SourceUrl: url));
                }

            // Actress
            var actressNodes = doc.DocumentNode.SelectNodes("//div[@class='star-name']/a");
            if (actressNodes != null)
                foreach (var a in actressNodes)
                {
                    var actressText = a.InnerText.Trim();
                    if (!string.IsNullOrEmpty(actressText))
                    {
                        results.Add(new MetadataCandidate(ProviderId, "Actress", actressText, 80, Priority, SourceUrl: url));
                        _logger.LogInformation("[JavBus] Actress={Actress}", actressText);
                    }
                }

            _logger.LogInformation("[JavBus] MetadataStatus=Success | Fields={Count}", results.Count);
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
            _logger.LogWarning(ex, "[JavBus] Network error");
            return MetadataResult.Failed(ProviderId, FailureReason.Network, ex.Message, sw.Elapsed, exception: ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[JavBus] Unexpected error");
            return MetadataResult.Failed(ProviderId, FailureReason.ProviderError, ex.Message, sw.Elapsed, exception: ex);
        }
    }
}
