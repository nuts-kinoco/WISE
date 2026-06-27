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
    [InlineData("FC2-PPV-1234567.mp4",  "FC2-PPV-1234567", "FC2Pattern")]
    [InlineData("FC2-PPV-9999999.mp4",  "FC2-PPV-9999999", "FC2Pattern")]
    public void ExtractCandidates_ShouldMatch_FC2Pattern(
        string fileName, string expectedValue, string expectedPattern)
    {
        var candidates = IdentifierParser.ExtractCandidates(fileName);
        candidates.Should().HaveCount(1);
        candidates[0].ExtractedValue.Should().Be(expectedValue);
        candidates[0].PatternName.Should().Be(expectedPattern);
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
