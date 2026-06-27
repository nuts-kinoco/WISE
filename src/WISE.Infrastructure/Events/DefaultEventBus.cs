using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WISE.Domain.Events;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Events;

public class DefaultEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;

    public DefaultEventBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent == null) throw new ArgumentNullException(nameof(domainEvent));

        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        
        var EnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        var handlers = (IEnumerable<object>)_serviceProvider.GetService(EnumerableType)!;

        if (handlers != null)
        {
            var tasks = handlers.Select(handler => 
            {
                var method = handlerType.GetMethod("HandleAsync");
                return (Task)method!.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
            });

            await Task.WhenAll(tasks);
        }
    }
}
