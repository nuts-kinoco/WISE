using System;

namespace WISE.Application.Queries;

public record WorkCardDto(
    Guid WorkId,
    string Title,
    string Identifier,
    string CoverImagePath,
    bool IsFavorite,
    bool HasConflict
);
