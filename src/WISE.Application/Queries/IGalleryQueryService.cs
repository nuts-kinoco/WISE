using System.Collections.Generic;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

public interface IGalleryQueryService
{
    Task<IEnumerable<WorkCardDto>> GetRecentWorksAsync(int count);
    Task<IEnumerable<WorkCardDto>> SearchWorksAsync(string query);
}
