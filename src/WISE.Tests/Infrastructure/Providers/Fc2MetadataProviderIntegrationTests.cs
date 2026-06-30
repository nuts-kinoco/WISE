using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using HtmlAgilityPack;
using WISE.Domain.Models;
using WISE.Infrastructure.Providers;
using Xunit;

namespace WISE.Tests.Infrastructure.Providers;

/// <summary>
/// FC2 Contents Market への実際の HTTP リクエストを伴う統合テスト。
/// CI では [Trait("Category", "Integration")] でスキップ可能。
/// </summary>
[Trait("Category", "Integration")]
public class Fc2MetadataProviderIntegrationTests
{
    private static readonly HttpClient HttpClient = new();

    private static async Task<List<MetadataCandidate>> FetchAndExtract(string numericId)
    {
        var url = $"https://adult.contents.fc2.com/article/{numericId}/";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        req.Headers.Add("Accept-Language", "ja,en;q=0.9");

        var resp = await HttpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var html = await resp.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var candidates = new List<MetadataCandidate>();
        Fc2MetadataProvider.ExtractMetadata(doc, url, candidates);
        return candidates;
    }

    [Fact]
    public async Task FC2_4847465_ShouldExtract_TitlePortraitCoverAndSampleImages()
    {
        var candidates = await FetchAndExtract("4847465");

        // タイトルが取れる
        var title = candidates.FirstOrDefault(c => c.FieldName == "Title");
        title.Should().NotBeNull();
        title!.Value.Should().NotBeNullOrWhiteSpace();
        title.Value.Should().NotContain("FC2コンテンツマーケット"); // サフィックス除去済み

        // パッケージ画像 (PortraitCover)
        var cover = candidates.FirstOrDefault(c => c.FieldName == "PortraitCover");
        cover.Should().NotBeNull();
        cover!.Value.Should().StartWith("https://");

        // サンプル画像が1枚以上
        var samples = candidates.Where(c => c.FieldName == "SampleImage").ToList();
        samples.Should().NotBeEmpty("sample images should be present on this product page");
        samples.Should().AllSatisfy(s => s.Value.Should().StartWith("https://"));

        // タグ
        var genre = candidates.FirstOrDefault(c => c.FieldName == "Genre");
        genre.Should().NotBeNull();

        // 発売日
        var date = candidates.FirstOrDefault(c => c.FieldName == "ReleaseDate");
        date.Should().NotBeNull();
        date!.Value.Should().MatchRegex(@"\d{4}/\d{2}/\d{2}");

        // 販売者
        var maker = candidates.FirstOrDefault(c => c.FieldName == "Maker");
        maker.Should().NotBeNull();
    }
}
