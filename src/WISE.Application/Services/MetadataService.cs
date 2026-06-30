using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Application.Services;

public class MetadataService
{
    // Tier1 (Priority≥80) がこれら全フィールドを揃えた場合、下位Providerをスキップする。
    // PortraitCover は URL候補があっても実ダウンロードが失敗する場合があるため除外。
    // カバーは Tier2 でも補完できる方が安全。
    private static readonly string[] Tier1ExitFields = ["Title", "Actress", "Maker"];

    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(IEnumerable<IMetadataProvider> providers, ILogger<MetadataService> logger)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<MetadataResult>> CollectResultsAsync(MetadataProviderContext context)
    {
        _logger.LogInformation("[WISE.Metadata] Scan Started: {Identifier}", context.Identifier);

        var ordered = _providers.OrderByDescending(p => p.Priority).ToList();

        // Tier1: 公式一次ソース (Priority≥80)。FANZAなど。
        var tier1 = ordered.Where(p => p.Priority >= 80).ToList();
        // Tier2+: 補完ソース (Priority<80)。MGS・Fc2・AvWikiなど。
        var tier2 = ordered.Where(p => p.Priority < 80).ToList();

        var allResults = new List<MetadataResult>();

        // --- Step1: Tier1 を並列実行 ---
        if (tier1.Count > 0)
        {
            _logger.LogInformation("[WISE.Metadata] Running {Count} Tier1 providers: {Names}",
                tier1.Count, string.Join(", ", tier1.Select(p => p.ProviderId)));

            var tier1Results = await Task.WhenAll(tier1.Select(p => SafeFetchAsync(p, context)));
            allResults.AddRange(tier1Results);
            LogFailures(tier1Results);

            // 早期終了チェック: Tier1で必須フィールドが全て揃っていれば下位をスキップ
            var coveredFields = tier1Results
                .Where(r => r.Success)
                .SelectMany(r => r.Candidates)
                .Select(c => c.FieldName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = Tier1ExitFields.Where(f => !coveredFields.Contains(f)).ToArray();

            if (missing.Length == 0)
            {
                _logger.LogInformation(
                    "[WISE.Metadata] Tier1 satisfied all primary fields ({Fields}). Skipping {Count} lower-priority providers.",
                    string.Join(", ", Tier1ExitFields), tier2.Count);
                return allResults;
            }

            _logger.LogInformation(
                "[WISE.Metadata] Tier1 incomplete. Missing: [{Missing}]. Proceeding to Tier2+ ({Count} providers).",
                string.Join(", ", missing), tier2.Count);
        }

        // --- Step2: Tier2+ を並列実行 ---
        if (tier2.Count > 0)
        {
            _logger.LogInformation("[WISE.Metadata] Running {Count} Tier2+ providers: {Names}",
                tier2.Count, string.Join(", ", tier2.Select(p => p.ProviderId)));

            var tier2Results = await Task.WhenAll(tier2.Select(p => SafeFetchAsync(p, context)));
            allResults.AddRange(tier2Results);
            LogFailures(tier2Results);
        }

        return allResults;
    }

    private async Task<MetadataResult> SafeFetchAsync(IMetadataProvider provider, MetadataProviderContext context)
    {
        try
        {
            return await provider.FetchAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WISE.Metadata] Exception thrown by provider {ProviderId}", provider.ProviderId);
            return MetadataResult.Failed(provider.ProviderId, FailureReason.ProviderError, ex.Message, TimeSpan.Zero, exception: ex);
        }
    }

    private void LogFailures(MetadataResult[] results)
    {
        foreach (var r in results.Where(r => !r.Success))
            _logger.LogWarning("[WISE.Metadata] Provider: {Provider} | Result: {Reason} | Message: {Msg}",
                r.ProviderName, r.FailureReason, r.FailureMessage);
    }
}
