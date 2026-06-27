using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Jobs;

public class InMemoryJobQueue : IJobQueue
{
    private readonly Channel<JobExecution> _channel;

    public InMemoryJobQueue()
    {
        _channel = Channel.CreateUnbounded<JobExecution>();
    }

    public async Task<Guid> EnqueueAsync(string jobType, string payload, Guid? correlationId = null, Guid? targetWorkId = null, CancellationToken cancellationToken = default)
    {
        var jobDef = new JobDefinition
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Configuration = payload,
            TargetWorkId = targetWorkId,
            CreatedAt = DateTime.UtcNow
        };

        var jobExecution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobDefinitionId = jobDef.Id,
            CorrelationId = correlationId,
            Status = "Queued",
            QueuedAt = DateTime.UtcNow,
            JobDefinition = jobDef
        };

        await _channel.Writer.WriteAsync(jobExecution, cancellationToken);
        return jobExecution.Id;
    }

    public async Task<JobExecution> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
