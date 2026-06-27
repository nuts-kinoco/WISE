using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WISE.Application.Services;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Application.Jobs;

public class FetchMetadataJobHandler
{
    private readonly MetadataService _metadataService;
    private readonly IMetadataConflictResolver _conflictResolver;

    public FetchMetadataJobHandler(MetadataService metadataService, IMetadataConflictResolver conflictResolver)
    {
        _metadataService = metadataService;
        _conflictResolver = conflictResolver;
    }

    public async Task HandleAsync(string configuration, Guid targetWorkId, Guid? correlationId, CancellationToken cancellationToken)
    {
        var context = new MetadataProviderContext(
            WorkId: targetWorkId,
            Identifier: "TEST-001", // TODO: Extract from DB or Configuration
            ExistingMetadata: Array.Empty<MetadataField>(),
            Language: "ja-JP",
            CancellationToken: cancellationToken
        );

        // 2. 収集
        var candidates = await _metadataService.CollectCandidatesAsync(context);

        // 3. 解決
        var resolved = _conflictResolver.Resolve(candidates);

        // 4. 保存
        // (UnitOfWork等のRepositoryを経由して保存するが、今回は骨格のみ)
    }
}
