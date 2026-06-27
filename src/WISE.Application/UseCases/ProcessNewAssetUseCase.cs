using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;
using WISE.Domain.Events;
using WISE.Domain.SeedWork;
using WISE.Domain.ValueObjects;

namespace WISE.Application.UseCases;

public class ProcessNewAssetUseCase
{
    private readonly IIdentifierResolver _resolver;
    private readonly IWorkRepository _workRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _eventBus;

    public ProcessNewAssetUseCase(IIdentifierResolver resolver, IWorkRepository workRepository, IUnitOfWork unitOfWork, IEventBus eventBus)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _workRepository = workRepository ?? throw new ArgumentNullException(nameof(workRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task<ProcessNewAssetResult> ExecuteAsync(string filePath, string originalFilename, long fileSize, CancellationToken cancellationToken = default)
    {
        // 1. Assetの生成とイベントの発行
        var asset = new Asset(filePath, originalFilename, fileSize);
        asset.AddDomainEvent(new AssetRegisteredEvent(asset.Id, asset.FilePath));

        // 2. IdentifierResolverによる評価
        var result = await _resolver.ResolveAsync(asset, cancellationToken);
        asset.AddDomainEvent(new IdentifierResolvedEvent(
            asset.Id, 
            result.WorkId, 
            result.Decision.ToString(), 
            result.Confidence.Value));

        Work work;
        bool isNewWork = false;

        // 3. 評価結果に基づく分岐
        if (result.Decision == Decision.Existing && result.WorkId.HasValue)
        {
            var existingWork = await _workRepository.GetByIdAsync(result.WorkId.Value, cancellationToken);
            if (existingWork != null)
            {
                work = existingWork;
            }
            else
            {
                work = new Work();
                isNewWork = true;
            }
        }
        else
        {
            work = new Work();
            isNewWork = true;
            work.AddDomainEvent(new WorkCreatedEvent(work.Id, work.PrimaryIdentifier));
            await _workRepository.AddAsync(work, cancellationToken);
        }

        // 4. WorkにAssetを追加 (Aggregate Root経由)
        work.AddAsset(asset);

        // 5. DBへ保存
        await _unitOfWork.SaveEntitiesAsync(cancellationToken);

        // トランザクションコミット成功後にEventを発行する
        var allEvents = work.DomainEvents?.ToList() ?? new List<IDomainEvent>();
        if (asset.DomainEvents != null)
        {
            allEvents.AddRange(asset.DomainEvents);
        }

        foreach (var evt in allEvents)
        {
            await _eventBus.PublishAsync(evt, cancellationToken);
        }

        // Publish完了後にClearする
        work.ClearDomainEvents();
        asset.ClearDomainEvents();

        // 6. 結果の生成
        return new ProcessNewAssetResult
        {
            WorkId = work.Id,
            IsNewWork = isNewWork,
            Confidence = result.Confidence.Value,
            EvidenceSummary = result.Evidences.Select(e => $"{e.Type}: {e.Value} (Score: {e.Score.Value})").ToList()
        };
    }
}
