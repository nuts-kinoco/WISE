using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;

namespace WISE.Infrastructure.Data.Queries;

public class ReaderQueryService : IReaderQueryService
{
    private readonly WiseDbContext _db;

    public ReaderQueryService(WiseDbContext db) => _db = db;

    public Task<Work?> GetWorkWithAssetsAsync(Guid workId, CancellationToken ct = default)
        => _db.Works
            .Include(w => w.Assets)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
}
