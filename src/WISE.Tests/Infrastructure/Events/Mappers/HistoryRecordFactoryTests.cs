using System;
using FluentAssertions;
using WISE.Domain.Events;
using WISE.Infrastructure.Events.Mappers;
using Xunit;

namespace WISE.Tests.Infrastructure.Events.Mappers;

public class HistoryRecordFactoryTests
{
    [Fact]
    public void Create_WithWorkCreatedEvent_ShouldMapCorrectly()
    {
        // Arrange
        var workId = Guid.NewGuid();
        var domainEvent = new WorkCreatedEvent(workId, "test.mp4");

        // Act
        var record = HistoryRecordFactory.Create(domainEvent);

        // Assert
        record.Should().NotBeNull();
        record.EventId.Should().Be(domainEvent.EventId);
        record.EventType.Should().Be(nameof(WorkCreatedEvent));
        record.WorkId.Should().Be(workId);
        record.AssetId.Should().BeNull();
        record.Payload.Should().Contain(workId.ToString());
    }
}
