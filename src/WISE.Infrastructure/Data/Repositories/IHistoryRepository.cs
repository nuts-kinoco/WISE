using System.Threading;
using System.Threading.Tasks;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Data.Repositories;

public interface IHistoryRepository
{
    Task AddAsync(HistoryRecord record, CancellationToken cancellationToken = default);
}
