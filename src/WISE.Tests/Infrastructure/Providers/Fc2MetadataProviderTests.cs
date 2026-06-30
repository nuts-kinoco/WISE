using System.Collections.Generic;
using HtmlAgilityPack;
using WISE.Domain.Models;
using WISE.Infrastructure.Providers;
using Xunit;
using FluentAssertions;

namespace WISE.Tests.Infrastructure.Providers;

/// <summary>
/// Fc2MetadataProvider のユニットテスト。
/// 実際の HTML を文字列で与え、ExtractMetadata() の出力を検証する。
/// </summary>
public class Fc2MetadataProviderTests
{
    // -------- ヘルパー --------

    private static List<MetadataCandidate> Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var candidates = new List<MetadataCandidate>();
        Fc2MetadataProvider.ExtractMetadata(doc, "https://adult.contents.fc2.com/article/4847465/", candidates);
        return candidates;
    }

    // -------- NormalizeThumbnailUrl --------

    [Theory]
    [InlineData(
        "//contents-thumbnail2.fc2.com/w1280/storage200000.contents.fc2.com/file/359/35808353/abc.png",
        "https://storage200000.contents.fc2.com/file/359/35808353/abc.png")]
    [InlineData(
        "//contents-thumbnail2.fc2.com/w480/storage201000.contents.fc2.com/file/1/2/3.png",
        "https://storage201000.contents.fc2.com/file/1/2/3.png")]
    [InlineData(
        "https://storage200000.contents.fc2.com/file/359/35808353/abc.png",
        "https://storage200000.contents.fc2.com/file/359/35808353/abc.png")]
    public void NormalizeThumbnailUrl_ShouldConvert_CdnUrlToStorage(string input, string expected)
    {
        Fc2MetadataProvider.NormalizeThumbnailUrl(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeThumbnailUrl_ShouldReturnNull_ForEmpty()
    {
        Fc2MetadataProvider.NormalizeThumbnailUrl("").Should().BeNull();
        Fc2MetadataProvider.NormalizeThumbnailUrl(null!).Should().BeNull();
    }

    // -------- Title --------

    [Fact]
    public void ExtractMetadata_ShouldExtractTitle_FromOgTitle()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="FC2-PPV-4847465 処女デビュー" />
            </head><body></body></html>
            """;

        var candidates = Parse(html);
        candidates.Should().Contain(c => c.FieldName == "Title" && c.Value == "処女デビュー");
    }

    [Fact]
    public void ExtractMetadata_ShouldStripSiteSuffix_FromTitle()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="サンプルタイトル - FC2コンテンツマーケット" />
            </head><body></body></html>
            """;

        var candidates = Parse(html);
        candidates.Should().Contain(c => c.FieldName == "Title" && c.Value == "サンプルタイトル");
    }

    // -------- PortraitCover --------

    [Fact]
    public void ExtractMetadata_ShouldExtractPortraitCover_FromOgImage()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="Test" />
            <meta property="og:image" content="https://storage200000.contents.fc2.com/file/359/35808353/1770791435.png" />
            </head><body></body></html>
            """;

        var candidates = Parse(html);
        candidates.Should().Contain(c =>
            c.FieldName == "PortraitCover" &&
            c.Value == "https://storage200000.contents.fc2.com/file/359/35808353/1770791435.png");
    }

    // -------- SampleImages --------

    [Fact]
    public void ExtractMetadata_ShouldExtractSampleImages_FromSampleSection()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="Test" />
            </head><body>
            <section class="items_article_SampleImages">
              <h3>サンプル画像</h3>
              <ul class="items_article_SampleImagesArea" data-feed="sample-images">
                <li><a href="//contents-thumbnail2.fc2.com/w1280/storage200000.contents.fc2.com/file/1/2/img1.png" data-image-slideshow="sample-images"></a></li>
                <li><a href="//contents-thumbnail2.fc2.com/w1280/storage201000.contents.fc2.com/file/1/2/img2.png" data-image-slideshow="sample-images"></a></li>
                <li><a href="//contents-thumbnail2.fc2.com/w1280/storage201000.contents.fc2.com/file/1/2/img3.png" data-image-slideshow="sample-images"></a></li>
              </ul>
            </section>
            </body></html>
            """;

        var candidates = Parse(html);
        var samples = candidates.Where(c => c.FieldName == "SampleImage").ToList();
        samples.Should().HaveCount(3);
        samples[0].Value.Should().Be("https://storage200000.contents.fc2.com/file/1/2/img1.png");
        samples[1].Value.Should().Be("https://storage201000.contents.fc2.com/file/1/2/img2.png");
        samples[2].Value.Should().Be("https://storage201000.contents.fc2.com/file/1/2/img3.png");
    }

    [Fact]
    public void ExtractMetadata_ShouldReturnNoSamples_WhenSectionAbsent()
    {
        const string html = """
            <html><head><meta property="og:title" content="Test" /></head><body></body></html>
            """;
        var candidates = Parse(html);
        candidates.Should().NotContain(c => c.FieldName == "SampleImage");
    }

    // -------- Genre (Tags) --------

    [Fact]
    public void ExtractMetadata_ShouldExtractGenres_FromTagSection()
    {
        const string html = """
            <html><head><meta property="og:title" content="Test" /></head><body>
            <section class="items_article_TagArea">
              <a class="tag tagTag" href="/search/?tag=ハメ撮り">ハメ撮り</a>
              <a class="tag tagTag" href="/search/?tag=素人">素人</a>
              <a class="tag tagTag" href="/search/?tag=美乳">美乳</a>
            </section>
            </body></html>
            """;

        var candidates = Parse(html);
        var genre = candidates.FirstOrDefault(c => c.FieldName == "Genre");
        genre.Should().NotBeNull();
        genre!.Value.Should().Contain("ハメ撮り");
        genre.Value.Should().Contain("素人");
        genre.Value.Should().Contain("美乳");
        genre.Value.Should().Contain("|");
    }

    // -------- ReleaseDate --------

    [Fact]
    public void ExtractMetadata_ShouldExtractReleaseDate()
    {
        const string html = """
            <html><head><meta property="og:title" content="Test" /></head><body>
            <div class="items_article_softDevice"><p>販売日 : 2026/02/12</p></div>
            </body></html>
            """;

        var candidates = Parse(html);
        candidates.Should().Contain(c => c.FieldName == "ReleaseDate" && c.Value == "2026/02/12");
    }

    // -------- Maker (Seller) --------

    [Fact]
    public void ExtractMetadata_ShouldExtractMaker_FromSellerLink()
    {
        const string html = """
            <html><head><meta property="og:title" content="Test" /></head><body>
            <ul><li>by <a href="https://adult.contents.fc2.com/users/jyuuoumujin/">汁王無尽</a></li></ul>
            </body></html>
            """;

        var candidates = Parse(html);
        candidates.Should().Contain(c => c.FieldName == "Maker" && c.Value == "汁王無尽");
    }

    // -------- Full page simulation --------

    [Fact]
    public void ExtractMetadata_ShouldExtractAllFields_FromTypicalPage()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="FC2-PPV-4847465 処女デビュー" />
            <meta property="og:image" content="https://storage200000.contents.fc2.com/file/1/2/cover.png" />
            </head><body>
            <ul><li>by <a href="https://adult.contents.fc2.com/users/testuser/">TestUser</a></li></ul>
            <section class="items_article_TagArea">
              <a class="tag tagTag">ハメ撮り</a>
              <a class="tag tagTag">素人</a>
            </section>
            <div class="items_article_softDevice"><p>販売日 : 2026/02/12</p></div>
            <section class="items_article_SampleImages">
              <ul class="items_article_SampleImagesArea" data-feed="sample-images">
                <li><a href="//contents-thumbnail2.fc2.com/w1280/storage200000.contents.fc2.com/file/1/2/s1.png" data-image-slideshow="sample-images"></a></li>
                <li><a href="//contents-thumbnail2.fc2.com/w1280/storage201000.contents.fc2.com/file/1/2/s2.png" data-image-slideshow="sample-images"></a></li>
              </ul>
            </section>
            </body></html>
            """;

        var candidates = Parse(html);

        candidates.Should().Contain(c => c.FieldName == "Title");
        candidates.Should().Contain(c => c.FieldName == "PortraitCover");
        candidates.Should().Contain(c => c.FieldName == "Maker" && c.Value == "TestUser");
        candidates.Should().Contain(c => c.FieldName == "Genre");
        candidates.Should().Contain(c => c.FieldName == "ReleaseDate" && c.Value == "2026/02/12");
        candidates.Where(c => c.FieldName == "SampleImage").Should().HaveCount(2);
    }
}
