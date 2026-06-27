using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WISE.Domain.Events;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Events;
using Xunit;

namespace WISE.Tests.Infrastructure.Events;

public class EventBusTests
{
    private class DummyEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private class DummyEventHandler : IDomainEventHandler<DummyEvent>
    {
        public bool IsHandled { get; private set; }

        public Task HandleAsync(DummyEvent domainEvent, CancellationToken cancellationToken = default)
        {
            IsHandled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_ShouldInvokeRegisteredHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new DummyEventHandler();
        services.AddSingleton<IDomainEventHandler<DummyEvent>>(handler);
        
        var serviceProvider = services.BuildServiceProvider();
        var eventBus = new DefaultEventBus(serviceProvider);
        var dummyEvent = new DummyEvent();

        // Act
        await eventBus.PublishAsync(dummyEvent);

        // Assert
        handler.IsHandled.Should().BeTrue();
    }
}
