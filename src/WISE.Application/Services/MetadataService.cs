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

    // MediaType ごとに「揃えば早期終了してよい」テキストフィールド（＝望ましいフィールド集合）。
    // これは分岐ロジックではなくデータ設定であり、識別子文字列による分岐を含まない。
    // FC2 等の個別事情は Provider 側の ProvidableFields 宣言で吸収する（下記 GetExitFields 参照）。
    // TODO(P1/将来): 究極的には MediaDisplayProfile 側の設定に寄せる余地がある。
    private static readonly IReadOnlyDictionary<MediaType, string[]> DesiredFieldsByMediaType =
        new Dictionary<MediaType, string[]>
        {
            [MediaType.Comic] = ["Title", "Author", "Circle"],
            [MediaType.Book] = ["Title", "Author"],
        };

    private static string[] GetDesiredFields(MediaType mediaType)
        => DesiredFieldsByMediaType.TryGetValue(mediaType, out var fields)
            ? fields
            : ["Title", "Actress", "Maker"]; // 既定（Video 等）

    /// <summary>
    /// 「取得を待つ意味のある」フィールド集合を算出する。
    /// 望ましいフィールドのうち、これまでに成功した Provider のいずれかが構造的に供給しうるものだけを対象とする。
    /// ProvidableFields が null の Provider は「全フィールド供給可能」とみなす。
    /// 例: FC2 では実際に成功するのは Fc2/Fc2Alt のみで、両者は Title のみを供給可能と宣言するため、
    ///     Actress/Maker は「誰も供給できない」と判定され早期終了を妨げない（識別子文字列の分岐は不要）。
    /// </summary>
    private static string[] GetExitFields(
        string[] desiredFields,
        IEnumerable<IMetadataProvider> succeededProviders)
    {
        var providable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in succeededProviders)
        {
            if (p.ProvidableFields == null)
                return desiredFields; // 制約なしの Provider が1つでも成功していれば全 desired を要求
            foreach (var f in p.ProvidableFields) providable.Add(f);
        }
        return desiredFields.Where(providable.Contains).ToArray();
    }

    private static readonly string[] CoverFields = ["PortraitCover", "LandscapeCover"];

    public async Task<IEnumerable<MetadataResult>> CollectResultsAsync(MetadataProviderContext context)
    {
        _logger.LogInformation("[WISE.Metadata] Scan Started: {Identifier} MediaType={MediaType}",
            context.Identifier, context.MediaType);

        var eligibleProviders = _providers
            .Where(p => p.SupportedMediaTypes == null || p.SupportedMediaTypes.Contains(context.MediaType))
            .Where(p => p.CanHandle(context.Identifier))
            .ToList();

        var groups = eligibleProviders
            .GroupBy(p => p.Priority)
            .OrderByDescending(g => g.Key)
            .ToList();

        // 成功した MetadataResult（ProviderName に ProviderId が入る）から Provider を引くための対応表
        var providerById = eligibleProviders
            .GroupBy(p => p.ProviderId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allResults = new List<MetadataResult>();
        var desiredFields = GetDesiredFields(context.MediaType);
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

            var succeeded = allResults.Where(r => r.Success).ToList();

            var covered = succeeded
                .SelectMany(r => r.Candidates)
                .Select(c => c.FieldName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // これまで成功した Provider が構造的に供給しうるフィールドだけを終了条件とする
            var succeededProviders = succeeded
                .Select(r => providerById.TryGetValue(r.ProviderName, out var p) ? p : null)
                .Where(p => p != null)
                .Select(p => p!);
            var exitFields = GetExitFields(desiredFields, succeededProviders);

            var missing = exitFields.Where(f => !covered.Contains(f)).ToArray();
            // 成功 Provider が1つも無い段階では早期終了しない
            // （exitFields が空集合になり「何も取得せず満足」と誤判定するのを防ぐ）
            if (succeeded.Count > 0 && missing.Length == 0 && !textSatisfied)
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
