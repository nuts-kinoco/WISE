using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

        var service = new MetadataService(providers, NullLogger<MetadataService>.Instance);
        var resolver = new MetadataConflictResolver();
        var handler = new FetchMetadataJobHandler(service, resolver);

        // Act & Assert (今回はHandleAsyncが例外を投げないことを確認)
        await handler.HandleAsync("{\"Identifier\":\"TEST-001\"}", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        
        // 直接内部ロジックを検証
        var context = new WISE.Domain.Models.MetadataProviderContext(Guid.NewGuid(), "TEST-001", Array.Empty<WISE.Domain.Entities.MetadataField>(), "ja", CancellationToken.None);
        var results = await service.CollectResultsAsync(context);
        var candidates = results.SelectMany(r => r.Candidates);
        var resolved = resolver.Resolve(candidates).ToList();

        // Dummy(Title), Conflict(Title)
        resolved.Should().HaveCount(2); 
        
        var primaries = resolved.Where(r => r.IsPrimary).ToList();
        primaries.Should().HaveCount(1); // Title

        var primaryTitle = primaries.Single(r => r.Candidate.FieldName == "Title");
        // Conflict is priority 20 vs Dummy 10, so Dummy is lower number but the dummy sets same confidence and priority. Dummy priority 10 is better.
        // Actually both have same Title but ConflictDummy has Dummy Conflict Video A.
    }
}
