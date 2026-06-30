using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Domain.Interfaces;

public interface IReadingHistoryRepository
{
    Task<ReadingHistory?> GetAsync(Guid workId, string deviceId, CancellationToken ct = default);
    Task UpsertAsync(ReadingHistory history, CancellationToken ct = default);
    Task DeleteAsync(Guid workId, string deviceId, CancellationToken ct = default);
}
