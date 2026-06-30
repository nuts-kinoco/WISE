using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class ReadingHistory : Entity
{
    public Guid WorkId { get; private set; }
    public string DeviceId { get; private set; }
    public int? PageNumber { get; private set; }
    public float? PositionSeconds { get; private set; }
    public float? PositionPercent { get; private set; }
    public DateTime LastReadAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public virtual Work? Work { get; private set; }

    protected ReadingHistory()
    {
        DeviceId = string.Empty;
    }

    public ReadingHistory(Guid workId, string deviceId, int? pageNumber = null, float? positionSeconds = null, float? positionPercent = null)
    {
        Id = Guid.NewGuid();
        WorkId = workId;
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        PageNumber = pageNumber;
        PositionSeconds = positionSeconds;
        PositionPercent = positionPercent;
        LastReadAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProgress(int? pageNumber, float? positionSeconds, float? positionPercent)
    {
        PageNumber = pageNumber;
        PositionSeconds = positionSeconds;
        PositionPercent = positionPercent;
        LastReadAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
