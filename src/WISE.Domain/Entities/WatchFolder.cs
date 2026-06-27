using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class WatchFolder : Entity
{
    public string Path { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastScannedAt { get; private set; }

    // Future Extensions (Null allowed)
    public Guid? MetadataPipelineProfileId { get; set; }
    public Guid? RuleProfileId { get; set; }
    public Guid? IdentifierProfileId { get; set; }

    protected WatchFolder()
    {
        Path = string.Empty;
    }

    public WatchFolder(string path)
    {
        Id = Guid.NewGuid();
        Path = path ?? throw new ArgumentNullException(nameof(path));
        IsEnabled = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public void RecordScan()
    {
        LastScannedAt = DateTime.UtcNow;
    }
}
