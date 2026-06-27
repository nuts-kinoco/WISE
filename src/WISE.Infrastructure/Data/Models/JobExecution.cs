using System;

namespace WISE.Infrastructure.Data.Models;

public class JobExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobDefinitionId { get; set; }
    public Guid? CorrelationId { get; set; }
    public string Status { get; set; } = "Queued";
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    public JobDefinition JobDefinition { get; set; } = null!;
}
