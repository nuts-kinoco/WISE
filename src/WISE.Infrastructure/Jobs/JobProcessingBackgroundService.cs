using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace WISE.Infrastructure.Jobs;

public class JobProcessingBackgroundService : BackgroundService
{
    private readonly InMemoryJobQueue _jobQueue;

    public JobProcessingBackgroundService(InMemoryJobQueue jobQueue)
    {
        _jobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobExecution = await _jobQueue.DequeueAsync(stoppingToken);
                
                // JobExecution processing logic here
                // Note: Normally we would resolve IJobHandler from IServiceProvider and execute
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception)
            {
                // Log and continue processing the next item
            }
        }
    }
}
