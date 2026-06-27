using System.Threading.Tasks;

namespace WISE.Application.Common;

public interface IEventBus
{
    Task PublishAsync<T>(T @event) where T : class;
}
