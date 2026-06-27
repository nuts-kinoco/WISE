using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Interfaces;

public interface IWorkRepository : IRepository<Work>
{
    Task<Work?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Work> AddAsync(Work work, CancellationToken cancellationToken = default);
    Task UpdateAsync(Work work, CancellationToken cancellationToken = default);
    Task DeleteAsync(Work work, CancellationToken cancellationToken = default);
}
