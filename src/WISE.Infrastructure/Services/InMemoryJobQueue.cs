using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Application.DTOs;
using WISE.Application.Interfaces;

namespace WISE.Infrastructure.Services;

public class InMemoryJobQueue : IJobQueue
{
    private readonly ILogger<InMemoryJobQueue> _logger;

    public InMemoryJobQueue(ILogger<InMemoryJobQueue> logger)
    {
        _logger = logger;
    }

    public Task<string> EnqueueImportJobAsync(ImportJobRequest request)
    {
        var jobId = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        // V1.0 Dummy Implementation: 
        // In reality, this would serialize the request and insert it into JOB table or RabbitMQ.
        _logger.LogInformation("Job #{JobId} enqueued for import. Mode: {Mode}, Input count: {InputCount}", 
            jobId, request.ImportMode, request.InputFolders.Count);

        return Task.FromResult(jobId);
    }
}
