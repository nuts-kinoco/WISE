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
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(IEnumerable<IMetadataProvider> providers, ILogger<MetadataService> logger)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string[] GetExitFields(MediaType mediaType, string identifier)
    {
        if (mediaType == MediaType.Comic) return ["Title", "Author", "Circle"];
        if (mediaType == MediaType.Book) return ["Title", "Author"];

        // Video specific logic based on identifier
        if (!string.IsNullOrEmpty(identifier) && identifier.StartsWith("FC2", StringComparison.OrdinalIgnoreCase))
        {
            return ["Title"]; // FC2 usually lacks Maker/Actress structurally
        }

        return ["Title", "Actress", "Maker"];
    }

    private static readonly string[] CoverFields = ["PortraitCover", "LandscapeCover"];

    public async Task<IEnumerable<MetadataResult>> CollectResultsAsync(MetadataProviderContext context)
    {
        _logger.LogInformation("[WISE.Metadata] Scan Started: {Identifier} MediaType={MediaType}",
            context.Identifier, context.MediaType);

        var groups = _providers
            .Where(p => p.SupportedMediaTypes == null || p.SupportedMediaTypes.Contains(context.MediaType))
            .Where(p => p.CanHandle(context.Identifier))
            .GroupBy(p => p.Priority)
            .OrderByDescending(g => g.Key)
            .ToList();

        var allResults = new List<MetadataResult>();
        var exitFields = GetExitFields(context.MediaType, context.Identifier);
        bool textSatisfied = false;
        int textSatisfiedAtIndex = groups.Count; // 早期終了した位置

        // --- Phase 1: テキスト系フィールド収集（Priority グループ単位、早期終了あり）---
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var providers = group.ToList();
            _logger.LogInformation(
                "[WISE.Metadata] Running Priority={Priority} group ({Count} providers): {Names}",
                group.Key, providers.Count, string.Join(", ", providers.Select(p => p.ProviderId)));

            var groupResults = await Task.WhenAll(providers.Select(p => SafeFetchAsync(p, context)));
            allResults.AddRange(groupResults);
            LogFailures(groupResults);

            var covered = allResults
                .Where(r => r.Success)
                .SelectMany(r => r.Candidates)
                .Select(c => c.FieldName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = exitFields.Where(f => !covered.Contains(f)).ToArray();
            if (missing.Length == 0 && !textSatisfied)
            {
                textSatisfied = true;
                textSatisfiedAtIndex = i;
                _logger.LogInformation(
                    "[WISE.Metadata] Text fields satisfied at Priority={Priority}. Checking cover candidates.",
                    group.Key);
                break;
            }

            if (missing.Length > 0)
                _logger.LogInformation(
                    "[WISE.Metadata] Missing after Priority={Priority}: [{Missing}]",
                    group.Key, string.Join(", ", missing));
        }

        // --- Phase 2: カバー専用フォールバック（テキスト早期終了した続きのグループのみ）---
        // テキストは揃っているが、未実行グループのカバーを掘り下げる
        if (textSatisfied)
        {
            var hasCover = allResults
                .Where(r => r.Success)
                .SelectMany(r => r.Candidates)
                .Any(c => CoverFields.Contains(c.FieldName));

            if (!hasCover)
                _logger.LogInformation("[WISE.Metadata] No cover candidate yet. Running cover fallback pass.");

            // テキスト早期終了以降の未実行グループをカバー目的で走らせる
            for (int i = textSatisfiedAtIndex + 1; i < groups.Count; i++)
            {
                var group = groups[i];
                var providers = group.ToList();
                _logger.LogInformation(
                    "[WISE.Metadata] [CoverFallback] Priority={Priority} group ({Count} providers): {Names}",
                    group.Key, providers.Count, string.Join(", ", providers.Select(p => p.ProviderId)));

                var groupResults = await Task.WhenAll(providers.Select(p => SafeFetchAsync(p, context)));
                allResults.AddRange(groupResults);
                LogFailures(groupResults);

                // カバー候補が見つかったら終了
                var coverCandidates = groupResults
                    .Where(r => r.Success)
                    .SelectMany(r => r.Candidates)
                    .Where(c => CoverFields.Contains(c.FieldName))
                    .ToList();

                if (coverCandidates.Count > 0)
                {
                    _logger.LogInformation(
                        "[WISE.Metadata] [CoverFallback] Cover found at Priority={Priority}. Stopping fallback.",
                        group.Key);
                    break;
                }
            }
        }
        else
        {
            // テキスト早期終了しなかった場合 (全グループ実行済み) は追加アクション不要
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
