using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Application.Queries;

/// <summary>
/// ホーム画面（ダッシュボード）向けの読み取り専用クエリ。
/// P1 リファクタリング Phase2: HomeController から WiseDbContext 直接参照を排除するための Query サービス。
/// マッピング（WorkItemMapper.Map によるAPIレスポンス整形）は Api 層の責務のため、
/// ここでは Domain の Work エンティティをそのまま返す。
/// </summary>
public interface IHomeQueryService
{
    Task<List<Work>> GetContinueWatchingAsync(string? deviceId, CancellationToken ct = default);
    Task<List<Work>> GetRecentlyAddedAsync(CancellationToken ct = default);
    Task<List<Work>> GetFavoritesAsync(CancellationToken ct = default);
    Task<Work?> GetRandomAsync(CancellationToken ct = default);
}
