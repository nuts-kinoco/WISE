using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

public record HistoryItemDto(
    Guid Id,
    DateTime OccurredAt,
    string EventType,
    string Payload,
    string WorkTitle,
    string Identifier
);

public interface IHistoryQueryService
{
    Task<IEnumerable<HistoryItemDto>> GetRecentHistoryAsync(int count);
}
