using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Application.Queries;

/// <summary>
/// Job（非同期処理キュー）の読み取り専用クエリ。
/// P1 リファクタリング Phase4: JobsController から WiseDbContext 直接参照を排除するための Query サービス。
/// </summary>
public interface IJobsQueryService
{
    Task<IReadOnlyList<Job>> GetRecentAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveJobDto>> GetActiveAsync(CancellationToken ct = default);
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

// Status は文字列で公開する（フロントが "Running"/"Queued" 等の文字列一致で判定しているため、
// デフォルトのenum数値シリアライズは使わない。JobsController.GetJobs の生Job返却も同様の理由でenumのまま）
public record ActiveJobDto(
    Guid Id,
    string JobType,
    string Status,
    string? Target,
    string? Identifier,
    DateTime CreatedAt,
    DateTime? StartedAt,
    string? ErrorMessage
);
