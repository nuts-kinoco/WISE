using System;
using System.Text.Json;
using WISE.Domain.Enums;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class Job : Entity
{
    public string JobType { get; private set; }
    public JobStatus Status { get; private set; }
    public string Target { get; private set; }
    public string? Payload { get; private set; } // Stored as JSON string
    public string? ResultPayload { get; private set; } // Stored as JSON string
    public int TotalCount { get; private set; }
    public int ProcessedCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    protected Job()
    {
        JobType = string.Empty;
        Target = string.Empty;
    }

    public Job(string jobType, string target, string? payload = null)
    {
        Id = Guid.NewGuid();
        JobType = jobType ?? throw new ArgumentNullException(nameof(jobType));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Payload = payload;
        Status = JobStatus.Created;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsQueued()
    {
        Status = JobStatus.Queued;
    }

    public void MarkAsRunning()
    {
        Status = JobStatus.Running;
        StartedAt = DateTime.UtcNow;
    }
    
    public void UpdateProgress(int processed, int total)
    {
        ProcessedCount = processed;
        TotalCount = total;
    }

    public void MarkAsCompleted(string? resultPayload = null)
    {
        Status = JobStatus.Completed;
        ResultPayload = resultPayload;
        FinishedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string errorMessage)
    {
        Status = JobStatus.Failed;
        ErrorMessage = errorMessage;
        FinishedAt = DateTime.UtcNow;
    }

    public void MarkAsCanceled()
    {
        Status = JobStatus.Canceled;
        FinishedAt = DateTime.UtcNow;
    }
}
