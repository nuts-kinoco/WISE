using System;

namespace WISE.Infrastructure.Data.Models;

public class JobDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobType { get; set; } = string.Empty;
    public string Configuration { get; set; } = string.Empty;
    public Guid? TargetWorkId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
