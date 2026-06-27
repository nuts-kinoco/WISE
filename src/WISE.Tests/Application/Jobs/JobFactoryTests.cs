using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WISE.Application.Jobs;
using WISE.Domain.Events;
using WISE.Domain.Interfaces;
using Xunit;

namespace WISE.Tests.Application.Jobs;

public class JobFactoryTests
{
    [Fact]
    public async Task CreateAndEnqueueAsync_WithWorkCreatedEvent_ShouldEnqueueMetadataFetchJob()
    {
        // Arrange
        var mockQueue = new Mock<IJobQueue>();
        var factory = new JobFactory(mockQueue.Object);
        var workId = Guid.NewGuid();
        var domainEvent = new WorkCreatedEvent(workId, "test.mp4");

        // Act
        await factory.CreateAndEnqueueAsync(domainEvent, CancellationToken.None);

        // Assert
        mockQueue.Verify(q => q.EnqueueAsync(
            "MetadataFetch",
            It.Is<string>(s => s.Contains(workId.ToString())),
            domainEvent.EventId,
            workId,
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }
}
