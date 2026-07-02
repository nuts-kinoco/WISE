using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング（追加是正）: SystemController から WiseDbContext 直接参照を排除するための UseCase。
/// 設定ページ「メンテナンス」からの操作履歴一括削除を担う。
/// </summary>
public class SystemMaintenanceUseCase
{
    private readonly WiseDbContext _dbContext;

    public SystemMaintenanceUseCase(WiseDbContext dbContext) => _dbContext = dbContext;

    public async Task<int> ClearHistoryAsync(CancellationToken ct = default)
    {
        var count = await _dbContext.EventLogs.CountAsync(ct);
        await _dbContext.EventLogs.ExecuteDeleteAsync(ct);
        return count;
    }
}
