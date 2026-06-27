using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

public record JobDto(
    Guid JobId,
    string JobType,
    string Status,
    int Progress,
    string Message,
    DateTime CreatedAt
);

public interface IJobQueryService
{
    Task<IEnumerable<JobDto>> GetActiveJobsAsync();
    Task<IEnumerable<JobDto>> GetJobHistoryAsync(int count);
}
