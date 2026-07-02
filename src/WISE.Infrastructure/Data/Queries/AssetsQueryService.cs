using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;

namespace WISE.Infrastructure.Data.Queries;

public class AssetsQueryService : IAssetsQueryService
{
    private readonly WiseDbContext _db;

    public AssetsQueryService(WiseDbContext db) => _db = db;

    public Task<Asset?> GetByIdAsync(Guid assetId, CancellationToken ct = default)
        => _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct);
}
