using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using WISE.Application.Services;
using WISE.Domain.Models;
using Xunit;

namespace WISE.Tests.Application.Services;

public class MetadataConflictResolverTests
{
    private readonly MetadataConflictResolver _resolver;

    public MetadataConflictResolverTests()
    {
        _resolver = new MetadataConflictResolver();
    }

    [Fact]
    public void Resolve_ShouldSelectHighestConfidenceAsPrimary()
    {
        // Arrange
        var candidates = new List<MetadataCandidate>
        {
            new("ProviderA", "Title", "A title", 80, 50),
            new("ProviderB", "Title", "B title", 95, 50),
            new("ProviderC", "Title", "C title", 60, 50)
        };

        // Act
        var resolved = _resolver.Resolve(candidates).ToList();

        // Assert
        resolved.Should().HaveCount(3);
        
        var primary = resolved.Single(r => r.IsPrimary);
        primary.Candidate.ProviderId.Should().Be("ProviderB");
        primary.Candidate.Value.Should().Be("B title");

        var nonPrimaries = resolved.Where(r => !r.IsPrimary).ToList();
        nonPrimaries.Should().HaveCount(2);
        nonPrimaries.Select(r => r.Candidate.ProviderId).Should().Contain("ProviderA").And.Contain("ProviderC");
    }

    [Fact]
    public void Resolve_WhenConfidenceIsEqual_ShouldSelectNewestTimestamp()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddDays(-1);
        var newTime = DateTime.UtcNow;

        var candidates = new List<MetadataCandidate>
        {
            new("ProviderA", "Actress", "Actress A", 90, 50) { FetchedAt = oldTime },
            new("ProviderB", "Actress", "Actress B", 90, 50) { FetchedAt = newTime }
        };

        // Act
        var resolved = _resolver.Resolve(candidates).ToList();

        // Assert
        resolved.Should().HaveCount(2);
        var primary = resolved.Single(r => r.IsPrimary);
        primary.Candidate.ProviderId.Should().Be("ProviderB"); // Newer wins
    }

    [Fact]
    public void Resolve_ShouldResolvePerFieldName()
    {
        // Arrange
        var candidates = new List<MetadataCandidate>
        {
            new("ProvA", "Title", "T1", 90, 50),
            new("ProvB", "Title", "T2", 80, 50),
            new("ProvA", "Maker", "M1", 70, 50),
            new("ProvB", "Maker", "M2", 95, 50)
        };

        // Act
        var resolved = _resolver.Resolve(candidates).ToList();

        // Assert
        resolved.Should().HaveCount(4);
        var primaries = resolved.Where(r => r.IsPrimary).ToList();
        primaries.Should().HaveCount(2);

        primaries.Single(r => r.Candidate.FieldName == "Title").Candidate.Value.Should().Be("T1");
        primaries.Single(r => r.Candidate.FieldName == "Maker").Candidate.Value.Should().Be("M2");
    }
}

