using System.Linq;
using System.Threading.Tasks;
using WISE.Domain.Services;
using Xunit;
using FluentAssertions;

namespace WISE.Tests.Domain;

/// <summary>
/// Sprint 13 Regression Test - Identifier Resolution
/// IdentifierParser.ExtractCandidates() と IdentifierResolver を検証する。
/// </summary>
public class IdentifierParserTests
{
    // ===== CommercialVideoPattern =====

    [Theory]
    [InlineData("IPX-001.mp4",    "IPX-001",   "CommercialVideoPattern")]
    [InlineData("SSNI-999.mp4",   "SSNI-999",  "CommercialVideoPattern")]
    [InlineData("SONE-123.mp4",   "SONE-123",  "CommercialVideoPattern")]
    [InlineData("EKDV-775.mp4",   "EKDV-775",  "CommercialVideoPattern")]
    [InlineData("FTAV-012.mp4",   "FTAV-012",  "CommercialVideoPattern")]
    public void ExtractCandidates_ShouldMatch_CommercialPattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    // ===== FC2Pattern =====

    [Theory]
    [InlineData("FC2-PPV-1234567.mp4",       "FC2-PPV-1234567", "FC2Pattern")]
    [InlineData("FC2-PPV-9999999.mp4",       "FC2-PPV-9999999", "FC2Pattern")]
    // FC2-NNNNNNN (PPV なし) → FC2-PPV-NNNNNNN に正規化
    [InlineData("FC2-9401364.mp4",           "FC2-PPV-9401364", "FC2Pattern")]
    [InlineData("FC2-1234567.mp4",           "FC2-PPV-1234567", "FC2Pattern")]
    [InlineData("fc2-9401364.mp4",           "FC2-PPV-9401364", "FC2Pattern")]
    // 連番サフィックス (-01, -02 など) は剥がして同一識別子に集約される
    [InlineData("FC2-PPV-4409072-01.mp4",    "FC2-PPV-4409072", "FC2Pattern")]
    [InlineData("FC2-PPV-4409072-02.mp4",    "FC2-PPV-4409072", "FC2Pattern")]
    [InlineData("FC2-PPV-4409072-03.mp4",    "FC2-PPV-4409072", "FC2Pattern")]
    [InlineData("fc2-ppv-4409072-01.mp4",    "FC2-PPV-4409072", "FC2Pattern")]  // lowercase
    [InlineData("FC2-PPV-4409072-01_720p.mp4", "FC2-PPV-4409072", "FC2Pattern")] // extra suffix
    public void ExtractCandidates_ShouldMatch_FC2Pattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    [Theory]
    [InlineData("FC2-PPV-4409072-01.mp4", "FC2-PPV-4409072")]
    [InlineData("FC2-PPV-4409072-02.mp4", "FC2-PPV-4409072")]
    public async Task IdentifierResolver_FC2SerialFiles_ShouldResolveToSameIdentifier(
        string fileName, string expectedIdentifier)
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset = new WISE.Domain.Entities.Asset($"/lib/{fileName}", fileName, 4_000_000_000L);
        var result = await resolver.ResolveAsync(asset);

        result.Decision.Should().Be(WISE.Domain.ValueObjects.Decision.New);
        result.ExtractedIdentifier.Should().Be(expectedIdentifier);
    }

    // ===== RJPattern =====

    [Theory]
    [InlineData("RJ123456.zip",   "RJ123456", "RJPattern")]
    [InlineData("RJ9999999.zip",  "RJ9999999","RJPattern")]
    public void ExtractCandidates_ShouldMatch_RJPattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    // ===== DLSiteVJBJPattern =====

    [Theory]
    [InlineData("VJ012345.zip",   "VJ012345", "DLSiteVJBJPattern")]
    [InlineData("BJ123456.cbz",   "BJ123456", "DLSiteVJBJPattern")]
    [InlineData("[VJ012345] ゲームタイトル.zip", "VJ012345", "DLSiteVJBJPattern")]
    public void ExtractCandidates_ShouldMatch_VJBJPattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    // ===== FanzaDoujinPattern =====

    [Theory]
    [InlineData("d_123456.zip",   "d_123456", "FanzaDoujinPattern")]
    [InlineData("d_987654.cbz",   "d_987654", "FanzaDoujinPattern")]
    public void ExtractCandidates_ShouldMatch_FanzaDoujinPattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    // ===== PathEvidenceProvider =====

    [Fact]
    public async Task PathEvidenceProvider_ShouldExtract_RJFromFolderName()
    {
        var provider = new WISE.Domain.Providers.PathEvidenceProvider();
        var asset = new WISE.Domain.Entities.Asset(
            @"C:\doujin\RJ123456 作品タイトル\RJ123456.cbz",
            "RJ123456.cbz", 1024L);

        var evidences = (await provider.CollectEvidencesAsync(asset)).ToList();

        evidences.Should().NotBeEmpty();
        evidences.Should().Contain(e => e.Value == "RJ123456");
    }

    [Fact]
    public async Task PathEvidenceProvider_ShouldReturnEmpty_ForUnknownFolderName()
    {
        var provider = new WISE.Domain.Providers.PathEvidenceProvider();
        var asset = new WISE.Domain.Entities.Asset(
            @"C:\doujin\Some Random Folder\file.cbz",
            "file.cbz", 1024L);

        var evidences = (await provider.CollectEvidencesAsync(asset)).ToList();

        evidences.Should().BeEmpty();
    }

    // ===== DatePattern =====

    [Theory]
    [InlineData("100123-001.mp4", "100123-001", "DatePattern")]
    public void ExtractCandidates_ShouldMatch_DatePattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
    }

    // ===== UNKNOWN（候補なし）=====

    [Theory]
    [InlineData("myvideo.mp4")]
    [InlineData("MyVacation2023.mp4")]
    [InlineData("DSC_0001.jpg")]
    public void ExtractCandidates_ShouldReturnEmpty_ForUnknown(string fileName)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().BeEmpty();
    }

    // ===== UNKNOWN 決定論的生成 =====

    [Fact]
    public async Task UNKNOWN_ShouldBeDeterministic_ForSameFileName()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset1 = new WISE.Domain.Entities.Asset("/path/myvideo.mp4", "myvideo.mp4", 1234567L);
        var asset2 = new WISE.Domain.Entities.Asset("/otherpath/myvideo.mp4", "myvideo.mp4", 1234567L);

        var result1 = await resolver.ResolveAsync(asset1);
        var result2 = await resolver.ResolveAsync(asset2);

        result1.ExtractedIdentifier.Should().Be(result2.ExtractedIdentifier);
        result1.ExtractedIdentifier.Should().StartWith("UNKNOWN-");
    }

    [Fact]
    public async Task UNKNOWN_ShouldBeDifferent_ForDifferentFileNames()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset1 = new WISE.Domain.Entities.Asset("/path/a.mp4", "a.mp4", 1024L);
        var asset2 = new WISE.Domain.Entities.Asset("/path/b.mp4", "b.mp4", 1024L);

        var result1 = await resolver.ResolveAsync(asset1);
        var result2 = await resolver.ResolveAsync(asset2);

        result1.ExtractedIdentifier.Should().NotBe(result2.ExtractedIdentifier);
    }

    // ===== IdentifierResolver - Evidence / Confidence / Decision 検証 =====

    [Fact]
    public async Task IdentifierResolver_ShouldReturnDecisionNew_ForEKDV()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset = new WISE.Domain.Entities.Asset("/path/EKDV-775.mp4", "EKDV-775.mp4", 2048L);
        var result = await resolver.ResolveAsync(asset);

        result.Decision.Should().Be(WISE.Domain.ValueObjects.Decision.New);
        result.ExtractedIdentifier.Should().Be("EKDV-775");
        result.Confidence.Value.Should().BeGreaterThanOrEqualTo(60);
        result.Evidences.Should().HaveCount(1);
        result.RejectReason.Should().BeNull();
    }

    [Fact]
    public async Task IdentifierResolver_ShouldReturnDecisionNew_ForFTAV()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset = new WISE.Domain.Entities.Asset("/path/FTAV-012.mp4", "FTAV-012.mp4", 2048L);
        var result = await resolver.ResolveAsync(asset);

        result.Decision.Should().Be(WISE.Domain.ValueObjects.Decision.New);
        result.ExtractedIdentifier.Should().Be("FTAV-012");
        result.Confidence.Value.Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public async Task IdentifierResolver_ShouldReturnDecisionNew_ForFC2()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset = new WISE.Domain.Entities.Asset("/path/FC2-PPV-1234567.mp4", "FC2-PPV-1234567.mp4", 2048L);
        var result = await resolver.ResolveAsync(asset);

        result.Decision.Should().Be(WISE.Domain.ValueObjects.Decision.New);
        result.ExtractedIdentifier.Should().Be("FC2-PPV-1234567");
    }

    [Fact]
    public async Task IdentifierResolver_ShouldReturnUnknown_WithRejectReason()
    {
        var resolver = new IdentifierResolver(
            new[] { new WISE.Domain.Providers.FileNameEvidenceProvider() });

        var asset = new WISE.Domain.Entities.Asset("/path/myvideo.mp4", "myvideo.mp4", 1024L);
        var result = await resolver.ResolveAsync(asset);

        result.Decision.Should().Be(WISE.Domain.ValueObjects.Decision.Unknown);
        result.ExtractedIdentifier.Should().StartWith("UNKNOWN-");
        result.RejectReason.Should().NotBeNullOrEmpty();
    }
}
