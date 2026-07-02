using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Application.Queries;

/// <summary>
/// Work（作品）の読み取り専用クエリ。
/// P1 リファクタリング Phase5: WorksController(819行)の解体で、検索/詳細/関連/カバー系の
/// 読取処理をここに集約する。WorkItemMapper.Map によるAPIレスポンス整形はApi層の責務のため、
/// リスト系メソッドは生の Work エンティティを返す（Home/Collections等これまでのPhaseと同じ境界）。
/// </summary>
public interface IWorksQueryService
{
    Task<(IReadOnlyList<Work> Works, int TotalCount)> GetListAsync(
        int page, int pageSize, string? q, string? status, string? mediaType, string? sort,
        CancellationToken ct = default);

    Task<WorkDetailQueryDto?> GetDetailAsync(Guid workId, CancellationToken ct = default);

    Task<IReadOnlyList<Work>> GetRelatedAsync(Guid workId, string? field, int limit, CancellationToken ct = default);

    /// <summary>viewer-info算出に必要な最小限のWork（MediaTypeのみ参照）を返す。</summary>
    Task<Work?> GetForViewerInfoAsync(Guid workId, CancellationToken ct = default);

    Task<IReadOnlyList<ThumbnailAssetDto>?> GetThumbnailAssetsAsync(Guid workId, CancellationToken ct = default);

    /// <summary>カバー配信・EPUB配信・フォルダを開く、等の読取専用アクションで共有するWork+Assets取得。</summary>
    Task<Work?> GetWorkWithAssetsAsync(Guid workId, CancellationToken ct = default);
}

public record WorkDetailQueryDto(
    Work Work,
    string? UserMemo,
    IReadOnlyList<string> SampleImages,
    IReadOnlyList<WorkHistoryEventDto> History,
    object? Diagnostic
);

public record WorkHistoryEventDto(string EventType, DateTime OccurredAt, string Actor, string? Payload);

public record ThumbnailAssetDto(
    Guid Id,
    string OriginalFilename,
    string AssetType,
    string Url,
    bool IsCurrentCover
);
