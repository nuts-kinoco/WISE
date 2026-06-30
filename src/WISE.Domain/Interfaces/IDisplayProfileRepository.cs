using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Domain.Interfaces;

public interface IDisplayProfileRepository
{
    Task<DisplayProfile?> GetByMediaTypeAsync(MediaType mediaType, CancellationToken ct = default);
    Task<IReadOnlyList<DisplayProfile>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(DisplayProfile profile, CancellationToken ct = default);
}
