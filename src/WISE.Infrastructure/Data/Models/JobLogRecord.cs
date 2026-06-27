using System;

namespace WISE.Infrastructure.Data.Models;

public class JobLogRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobExecutionId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string LogLevel { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
    
    public JobExecution JobExecution { get; set; } = null!;
}
