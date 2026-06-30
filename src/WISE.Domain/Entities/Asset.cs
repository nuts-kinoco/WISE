using System;
using WISE.Domain.Enums;
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
    public AssetType AssetType { get; private set; }
    public AssetRole Role { get; private set; }
    public StorageFormat StorageFormat { get; private set; }

    public virtual Work? Work { get; private set; }

    protected Asset()
    {
        FilePath = string.Empty;
        OriginalFilename = string.Empty;
        Status = "Active";
    }

    public Asset(string filePath, string originalFilename, long fileSize, string? sha256 = null, Guid? id = null, AssetType assetType = AssetType.Unknown, AssetRole role = AssetRole.Unknown, StorageFormat storageFormat = StorageFormat.SingleFile)
    {
        Id = id ?? Guid.NewGuid();
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        OriginalFilename = originalFilename ?? throw new ArgumentNullException(nameof(originalFilename));
        FileSize = fileSize;
        Sha256 = sha256;
        Status = "Active";
        DetectedAt = DateTime.UtcNow;
        AssetType = assetType;
        Role = role;
        StorageFormat = storageFormat;
    }

    public void LinkToWork(Work work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        WorkId = work.Id;
        Work = work;
    }

    public void UpdateFilePath(string newPath)
    {
        FilePath = newPath ?? throw new ArgumentNullException(nameof(newPath));
    }

    public void SetRole(AssetRole role) => Role = role;
    public void SetStorageFormat(StorageFormat format) => StorageFormat = format;
}
