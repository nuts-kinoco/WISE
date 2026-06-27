using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using WISE.Application.Jobs;
using WISE.Application.Services;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Providers;
using Xunit;

namespace WISE.Tests.Application.Jobs;

public class FetchMetadataJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldCollectAndResolveCorrectly()
    {
        // Arrange
        var providers = new IMetadataProvider[]
        {
            new DummyMetadataProvider(),
            new ConflictDummyMetadataProvider()
        };

        var service = new MetadataService(providers);
        var resolver = new MetadataConflictResolver();
        var handler = new FetchMetadataJobHandler(service, resolver);

        // Act & Assert (今回はHandleAsyncが例外を投げないことを確認)
        await handler.HandleAsync("{\"Identifier\":\"TEST-001\"}", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        
        // 直接内部ロジックを検証
        var context = new WISE.Domain.Models.MetadataProviderContext(Guid.NewGuid(), "TEST-001", Array.Empty<WISE.Domain.Entities.MetadataField>(), "ja", CancellationToken.None);
        var candidates = await service.CollectCandidatesAsync(context);
        var resolved = resolver.Resolve(candidates).ToList();

        // 4 candidates total: Dummy(Title, Actress), Conflict(Title, Maker)
        resolved.Should().HaveCount(4); 
        
        var primaries = resolved.Where(r => r.IsPrimary).ToList();
        primaries.Should().HaveCount(3); // Title, Actress, Maker

        // Title should be won by ConflictDummy due to higher Confidence (95 vs 80)
        var primaryTitle = primaries.Single(r => r.Candidate.FieldName == "Title");
        primaryTitle.Candidate.ProviderId.Should().Be("ConflictDummy");
        primaryTitle.Candidate.Value.Should().Be("Conflicting Title");
        
        // Check non-primary
        var nonPrimaryTitle = resolved.Single(r => !r.IsPrimary);
        nonPrimaryTitle.Candidate.FieldName.Should().Be("Title");
        nonPrimaryTitle.Candidate.ProviderId.Should().Be("Dummy");
    }
}
