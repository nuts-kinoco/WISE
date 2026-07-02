using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

/// <summary>
/// SystemController の EventLog 読み取り専用クエリ。
/// P1 リファクタリング（追加是正）: SystemController から WiseDbContext 直接参照を排除するための Query サービス。
/// 注意: フロントの実際の履歴画面は HistoryController(/api/history) を使用しており、
/// こちらの GetHistory/GetHistoryCount は現状フロントから未使用（ClearHistory のみ使用）。
/// 既存APIの後方互換のため削除はせず、DTO形状は変えずに維持する。
/// </summary>
public interface ISystemHistoryQueryService
{
    Task<IReadOnlyList<SystemHistoryEntryDto>> GetHistoryAsync(int limit, CancellationToken ct = default);
    Task<int> GetHistoryCountAsync(CancellationToken ct = default);
}

public record SystemHistoryEntryDto(
    Guid Id,
    DateTime Timestamp,
    string EventType,
    Guid? TargetWorkId,
    string? TargetWorkName,
    string? Summary
);
