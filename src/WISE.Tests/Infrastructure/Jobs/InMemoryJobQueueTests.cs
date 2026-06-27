using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using WISE.Infrastructure.Jobs;
using Xunit;

namespace WISE.Tests.Infrastructure.Jobs;

public class InMemoryJobQueueTests
{
    [Fact]
    public async Task EnqueueAndDequeue_ShouldWorkCorrectly()
    {
        // Arrange
        var queue = new InMemoryJobQueue();
        var correlationId = Guid.NewGuid();
        var targetWorkId = Guid.NewGuid();

        // Act
        var enqueuedJobId = await queue.EnqueueAsync("TestJob", "{\"key\":\"value\"}", correlationId, targetWorkId, CancellationToken.None);
        var dequeuedJob = await queue.DequeueAsync(CancellationToken.None);

        // Assert
        dequeuedJob.Should().NotBeNull();
        dequeuedJob.Id.Should().Be(enqueuedJobId);
        dequeuedJob.CorrelationId.Should().Be(correlationId);
        dequeuedJob.Status.Should().Be("Queued");
        dequeuedJob.JobDefinition.Should().NotBeNull();
        dequeuedJob.JobDefinition.JobType.Should().Be("TestJob");
        dequeuedJob.JobDefinition.Configuration.Should().Be("{\"key\":\"value\"}");
        dequeuedJob.JobDefinition.TargetWorkId.Should().Be(targetWorkId);
    }
}
