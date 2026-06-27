using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;
using WISE.Domain.Services;
using WISE.Domain.ValueObjects;
using Xunit;

namespace WISE.Tests.Domain;

/// <summary>
/// IdentifierResolver のテスト。
/// Sprint 13 の新設計に合わせて更新済み。
///
/// Decision.Existing は将来の DB 照合実装後に追加する。
/// v1.0 では Evidence が閾値以上なら Decision.New、以下なら Decision.Unknown を返す。
/// </summary>
public class IdentifierResolverTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnNew_WhenConfidenceIsAboveThreshold()
    {
        // Arrange: Score=85 の Evidence → Confidence=85 → Decision.New
        var asset = new Asset("/path/to/EKDV-775.mp4", "EKDV-775.mp4", 100);
        var mockProvider = new Mock<IEvidenceProvider>();
        mockProvider.Setup(p => p.CollectEvidencesAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Evidence>
            {
                new Evidence("CommercialVideoPattern", "EKDV-775", new ConfidenceScore(85), "MockProvider")
            });

        var resolver = new IdentifierResolver(new[] { mockProvider.Object });

        // Act
        var result = await resolver.ResolveAsync(asset);

        // Assert
        result.Decision.Should().Be(Decision.New);
        result.ExtractedIdentifier.Should().Be("EKDV-775");
        result.WorkId.Should().BeNull();
        result.Confidence.Value.Should().Be(85);
        result.RejectReason.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnknown_WhenConfidenceIsBelowThreshold()
    {
        // Arrange: Score=40（閾値60未満）→ Decision.Unknown
        var asset = new Asset("/path/to/myvideo.mp4", "myvideo.mp4", 100);
        var mockProvider = new Mock<IEvidenceProvider>();
        mockProvider.Setup(p => p.CollectEvidencesAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Evidence>
            {
                new Evidence("WeakHint", "myvideo", new ConfidenceScore(40), "MockProvider")
            });

        var resolver = new IdentifierResolver(new[] { mockProvider.Object });

        // Act
        var result = await resolver.ResolveAsync(asset);

        // Assert
        result.Decision.Should().Be(Decision.Unknown);
        result.ExtractedIdentifier.Should().StartWith("UNKNOWN-");
        result.Confidence.Value.Should().Be(40);
        result.RejectReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnknown_WhenNoEvidenceCollected()
    {
        // Arrange: Evidence なし → Score=0 → Decision.Unknown
        var asset = new Asset("/path/to/myvideo.mp4", "myvideo.mp4", 100);
        var mockProvider = new Mock<IEvidenceProvider>();
        mockProvider.Setup(p => p.CollectEvidencesAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Evidence>());

        var resolver = new IdentifierResolver(new[] { mockProvider.Object });

        // Act
        var result = await resolver.ResolveAsync(asset);

        // Assert
        result.Decision.Should().Be(Decision.Unknown);
        result.ExtractedIdentifier.Should().StartWith("UNKNOWN-");
        result.RejectReason.Should().Contain("No evidence");
    }
}
