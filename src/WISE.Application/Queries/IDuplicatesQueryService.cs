using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Application.Queries;

/// <summary>
/// 重複作品検出の読み取り専用クエリ。
/// P1 リファクタリング Phase4: DuplicatesController から WiseDbContext 直接参照を排除するための Query サービス。
/// </summary>
public interface IDuplicatesQueryService
{
    Task<IReadOnlyList<DuplicateGroupDto>> GetDuplicateGroupsAsync(CancellationToken ct = default);
}

public record DuplicateGroupDto(
    string Identifier,
    string DetectionType, // "identifier" | "title"
    IReadOnlyList<DuplicateWorkDto> Works
);

public record DuplicateWorkDto(
    Guid Id,
    string? PrimaryIdentifier,
    string Status,
    string? Title,
    string? Actress,
    string? Maker,
    bool Favorite,
    int? Rating,
    string? UserMemo,
    IReadOnlyList<DuplicateAssetDto> Assets
);

public record DuplicateAssetDto(
    Guid Id,
    string OriginalFilename,
    long? FileSize,
    string AssetType
);
