using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

/// <summary>
/// EventLog（操作履歴）の読み取り専用クエリ。
/// P1 リファクタリング: コントローラーから WiseDbContext 直接参照を排除するための Query サービス。
/// DTO 形状は現行 API レスポンス（フロント <c>HistoryDto</c>）に一致させている。
/// </summary>
public interface IHistoryQueryService
{
    Task<IReadOnlyList<HistoryEntryDto>> GetRecentHistoryAsync(int count, CancellationToken ct = default);
}

public record HistoryEntryDto(
    Guid Id,
    string EventType,
    string Actor,
    string Source,
    string? Payload,
    Guid? TargetId,
    string? TargetIdentifier,
    DateTime OccurredAt
);
