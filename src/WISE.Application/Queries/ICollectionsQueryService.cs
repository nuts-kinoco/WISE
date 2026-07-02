using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Application.Queries;

/// <summary>
/// Collection（作品グループ）の読み取り専用クエリ。
/// P1 リファクタリング Phase3: CollectionsController から WiseDbContext 直接参照を排除するための Query サービス。
/// 一覧はAPI表現に近い形（カバーURL計算済み）で返すが、詳細の Work マッピング（WorkItemMapper.Map）は
/// Api 層の責務のため、詳細取得では生の Work エンティティを返す。
/// </summary>
public interface ICollectionsQueryService
{
    Task<IReadOnlyList<CollectionSummaryDto>> GetAllAsync(CancellationToken ct = default);
    Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public record CollectionSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int ItemCount,
    string? CoverUrl
);

public record CollectionDetailDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CollectionDetailItemDto> Items
);

public record CollectionDetailItemDto(
    Guid ItemId,
    int Order,
    DateTime AddedAt,
    Work Work
);
