using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Application.Queries;

/// <summary>
/// Asset（ファイル実体）の読み取り専用クエリ。
/// P1 リファクタリング Phase3: AssetsController から WiseDbContext 直接参照を排除するための Query サービス。
/// </summary>
public interface IAssetsQueryService
{
    Task<Asset?> GetByIdAsync(Guid assetId, CancellationToken ct = default);
}
