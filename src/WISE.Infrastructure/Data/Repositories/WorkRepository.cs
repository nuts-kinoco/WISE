using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;

namespace WISE.Infrastructure.Data.Repositories;

public class WorkRepository : IWorkRepository
{
    private readonly WiseDbContext _context;

    public WorkRepository(WiseDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Work?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Works
            .Include(w => w.Assets)
            .Include(w => w.MetadataFields)
            .Include(w => w.EventLogs)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Work> AddAsync(Work work, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Works.AddAsync(work, cancellationToken);
        return entry.Entity;
    }

    public Task UpdateAsync(Work work, CancellationToken cancellationToken = default)
    {
        // Entity is already tracked by DbContext when fetched via GetByIdAsync.
        // But newly added children with generated GUIDs will be marked as Modified by default.
        // We explicitly mark untracked children as Added.
        foreach (var asset in work.Assets)
        {
            if (_context.Entry(asset).State == EntityState.Detached)
            {
                _context.Entry(asset).State = EntityState.Added;
            }
        }
        
        foreach (var field in work.MetadataFields)
        {
            if (_context.Entry(field).State == EntityState.Detached)
            {
                _context.Entry(field).State = EntityState.Added;
            }
        }
        
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Work work, CancellationToken cancellationToken = default)
    {
        _context.Works.Remove(work);
        return Task.CompletedTask;
    }
}
