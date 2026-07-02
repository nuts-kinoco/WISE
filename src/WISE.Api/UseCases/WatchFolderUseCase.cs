using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング: WatchFoldersController から WiseDbContext 直接参照を排除するための UseCase。
/// 監視フォルダの CRUD（一覧・作成・削除・有効/無効切替）を担う。
/// </summary>
public class WatchFolderUseCase
{
    private readonly WiseDbContext _dbContext;

    public WatchFolderUseCase(WiseDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WatchFolder>> GetAllAsync(CancellationToken ct = default)
        => await _dbContext.WatchFolders
            .AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

    public async Task<(bool Success, string? Error, WatchFolder? Created)> CreateAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Path is required.", null);

        if (await _dbContext.WatchFolders.AnyAsync(w => w.Path == path, ct))
            return (false, "Watch folder already exists.", null);

        var watchFolder = new WatchFolder(path);
        _dbContext.WatchFolders.Add(watchFolder);
        await _dbContext.SaveChangesAsync(ct);

        return (true, null, watchFolder);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await _dbContext.WatchFolders.FindAsync(new object[] { id }, ct);
        if (folder == null) return false;

        _dbContext.WatchFolders.Remove(folder);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<WatchFolder?> ToggleAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await _dbContext.WatchFolders.FindAsync(new object[] { id }, ct);
        if (folder == null) return null;

        if (folder.IsEnabled) folder.Disable();
        else folder.Enable();

        await _dbContext.SaveChangesAsync(ct);
        return folder;
    }
}
