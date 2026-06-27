using System;
using System.Collections.Generic;

namespace WISE.Application.Queries;

public record WorkDetailDto(
    Guid WorkId,
    string Title,
    string Identifier,
    string CoverImagePath,
    int ConfidenceScore,
    string Status,
    string MediaType,
    List<MetadataFieldDto> Metadata,
    List<AssetDto> Assets,
    List<HistoryDto> History,
    List<EvidenceDto> Evidence
);

public record MetadataFieldDto(
    string FieldName,
    string Value,
    string ProviderName,
    int Confidence,
    bool IsPrimary
);

public record AssetDto(
    Guid AssetId,
    string FilePath,
    long FileSizeBytes,
    string Resolution,
    string Duration,
    string Status
);

public record HistoryDto(
    DateTime OccurredAt,
    string EventType,
    string Details
);

public record EvidenceDto(
    string Strategy,
    int Score,
    string RawValue
);
