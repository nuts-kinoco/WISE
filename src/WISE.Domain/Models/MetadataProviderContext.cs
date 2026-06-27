using System;
using System.Collections.Generic;
using System.Threading;
using WISE.Domain.Entities;

namespace WISE.Domain.Models;

public record MetadataProviderContext(
    Guid WorkId,
    string Identifier,
    IReadOnlyCollection<MetadataField> ExistingMetadata,
    string Language,
    CancellationToken CancellationToken
);
