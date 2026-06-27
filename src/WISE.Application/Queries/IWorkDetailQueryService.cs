using System;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

public interface IWorkDetailQueryService
{
    Task<WorkDetailDto?> GetWorkDetailAsync(Guid workId);
}
