using System;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Domain.Interfaces;

public interface IJobScheduler
{
    Task<Guid> ScheduleAsync(string jobType, string payload, DateTimeOffset scheduleAt, Guid? correlationId = null, Guid? targetWorkId = null, CancellationToken cancellationToken = default);
}
