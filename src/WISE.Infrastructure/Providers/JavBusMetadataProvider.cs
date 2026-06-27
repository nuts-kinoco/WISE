using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class JavBusMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    
    public JavBusMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string ProviderId => "JavBus";
    public int Priority => 50;

    public async Task<IEnumerable<MetadataCandidate>> FetchAsync(MetadataProviderContext context)
    {
        var results = new List<MetadataCandidate>();
        
        // Rate limit: random delay 1-2 seconds
        await Task.Delay(new Random().Next(1000, 2000), context.CancellationToken);
        
        string url = $"https://www.javbus.com/en/{context.Identifier}";
        
        try
        {
            var response = await _httpClient.GetAsync(url, context.CancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return results;
            }

            var html = await response.Content.ReadAsStringAsync(context.CancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract Title
            var titleNode = doc.DocumentNode.SelectSingleNode("//h3");
            if (titleNode != null)
            {
                results.Add(new MetadataCandidate(ProviderId, "Title", titleNode.InnerText.Trim(), 80, url));
            }

            // Extract info from info block
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
                            results.Add(new MetadataCandidate(ProviderId, "Maker", maker, 80, url));
                    }
                    else if (text.Contains("Release Date:"))
                    {
                        var dateText = text.Replace("Release Date:", "").Trim();
                        if (!string.IsNullOrEmpty(dateText))
                            results.Add(new MetadataCandidate(ProviderId, "ReleaseDate", dateText, 80, url));
                    }
                }
            }

            // Extract Genres
            var genreNodes = doc.DocumentNode.SelectNodes("//span[@class='genre']/a[not(contains(@href, 'star'))]");
            if (genreNodes != null)
            {
                foreach (var g in genreNodes)
                {
                    // Filter out non-genre links if needed
                    results.Add(new MetadataCandidate(ProviderId, "Genre", g.InnerText.Trim(), 80, url));
                }
            }

            // Extract Actress
            var actressNodes = doc.DocumentNode.SelectNodes("//div[@class='star-name']/a");
            if (actressNodes != null)
            {
                foreach (var a in actressNodes)
                {
                    results.Add(new MetadataCandidate(ProviderId, "Actress", a.InnerText.Trim(), 80, url));
                }
            }
        }
        catch (Exception)
        {
            // Ignore errors to not break pipeline
        }

        return results;
    }
}
