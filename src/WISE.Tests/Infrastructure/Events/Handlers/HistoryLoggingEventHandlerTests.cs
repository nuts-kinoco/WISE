using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WISE.Domain.Events;
using WISE.Infrastructure.Data.Models;
using WISE.Infrastructure.Data.Repositories;
using WISE.Infrastructure.Events.Handlers;
using Xunit;

namespace WISE.Tests.Infrastructure.Events.Handlers;

public class HistoryLoggingEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldSaveHistoryRecord()
    {
        // Arrange
        var mockRepo = new Mock<IHistoryRepository>();
        var handler = new HistoryLoggingEventHandler(mockRepo.Object);
        var domainEvent = new WorkCreatedEvent(Guid.NewGuid(), "test.mp4");

        // Act
        await handler.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        mockRepo.Verify(r => r.AddAsync(It.IsAny<HistoryRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
