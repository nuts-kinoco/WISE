using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class Asset : Entity
{
    public Guid? WorkId { get; private set; }
    public string FilePath { get; private set; }
    public string OriginalFilename { get; private set; }
    public long FileSize { get; private set; }
    public string? Sha256 { get; private set; }
    public string Status { get; private set; }
    public DateTime DetectedAt { get; private set; }

    public virtual Work? Work { get; private set; }

    protected Asset() 
    {
        FilePath = string.Empty;
        OriginalFilename = string.Empty;
        Status = "Active";
    }

    public Asset(string filePath, string originalFilename, long fileSize, string? sha256 = null)
    {
        Id = Guid.NewGuid();
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        OriginalFilename = originalFilename ?? throw new ArgumentNullException(nameof(originalFilename));
        FileSize = fileSize;
        Sha256 = sha256;
        Status = "Active";
        DetectedAt = DateTime.UtcNow;
    }

    public void LinkToWork(Work work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        WorkId = work.Id;
        Work = work;
    }
}
