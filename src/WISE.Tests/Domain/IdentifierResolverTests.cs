using System;
using System.Collections.Generic;
using System.Linq;
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

public class IdentifierResolverTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnExisting_WhenConfidenceIsHigh()
    {
        // Arrange
        var asset = new Asset("/path/to/[Circle] Title.mp4", "[Circle] Title.mp4", 100);
        var mockProvider = new Mock<IEvidenceProvider>();
        mockProvider.Setup(p => p.CollectEvidencesAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Evidence>
            {
                new Evidence("Test", "TestValue", new ConfidenceScore(85), "MockProvider")
            });

        var resolver = new IdentifierResolver(new[] { mockProvider.Object });

        // Act
        var result = await resolver.ResolveAsync(asset);

        // Assert
        result.Decision.Should().Be(Decision.Existing);
        result.WorkId.Should().NotBeNull();
        result.Confidence.Value.Should().Be(85);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNew_WhenConfidenceIsLow()
    {
        // Arrange
        var asset = new Asset("/path/to/Title.mp4", "Title.mp4", 100);
        var mockProvider = new Mock<IEvidenceProvider>();
        mockProvider.Setup(p => p.CollectEvidencesAsync(asset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Evidence>
            {
                new Evidence("Test", "TestValue", new ConfidenceScore(40), "MockProvider")
            });

        var resolver = new IdentifierResolver(new[] { mockProvider.Object });

        // Act
        var result = await resolver.ResolveAsync(asset);

        // Assert
        result.Decision.Should().Be(Decision.New);
        result.WorkId.Should().BeNull();
        result.Confidence.Value.Should().Be(40);
    }
}
