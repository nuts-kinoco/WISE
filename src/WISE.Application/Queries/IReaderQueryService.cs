using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Application.Queries;

/// <summary>
/// コミック/書籍リーダー向けの読み取り専用クエリ。
/// P1 リファクタリング Phase3: ReaderController から WiseDbContext 直接参照を排除するための Query サービス。
/// </summary>
public interface IReaderQueryService
{
    Task<Work?> GetWorkWithAssetsAsync(Guid workId, CancellationToken ct = default);
}
