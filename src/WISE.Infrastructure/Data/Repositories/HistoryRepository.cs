using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Data.Repositories;

public class HistoryRepository : IHistoryRepository
{
    private readonly WiseDbContext _context;

    public HistoryRepository(WiseDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(HistoryRecord record, CancellationToken cancellationToken = default)
    {
        await _context.HistoryRecords.AddAsync(record, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
