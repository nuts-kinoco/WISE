using System;
using WISE.Domain.Enums;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class ProviderDiagnostic : Entity
{
    public string ProviderId { get; private set; }
    public string Strategy { get; private set; }
    public int SuccessCount { get; private set; }
    public int FailureCount { get; private set; }
    public long TotalResponseTimeMs { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public DateTime? LastFailureAt { get; private set; }
    public FailureReason? LastFailureReason { get; private set; }

    protected ProviderDiagnostic()
    {
        ProviderId = string.Empty;
        Strategy = string.Empty;
    }

    public ProviderDiagnostic(string providerId, string strategy)
    {
        Id = Guid.NewGuid();
        ProviderId = providerId;
        Strategy = strategy;
    }

    public void RecordSuccess(long responseTimeMs)
    {
        SuccessCount++;
        TotalResponseTimeMs += responseTimeMs;
        LastSuccessAt = DateTime.UtcNow;
    }

    public void RecordFailure(long responseTimeMs, FailureReason? reason)
    {
        FailureCount++;
        TotalResponseTimeMs += responseTimeMs;
        LastFailureAt = DateTime.UtcNow;
        LastFailureReason = reason;
    }

    public double AverageResponseTimeMs => (SuccessCount + FailureCount) == 0 ? 0 : TotalResponseTimeMs / (double)(SuccessCount + FailureCount);
    public double SuccessRate => (SuccessCount + FailureCount) == 0 ? 0 : SuccessCount / (double)(SuccessCount + FailureCount);
}
