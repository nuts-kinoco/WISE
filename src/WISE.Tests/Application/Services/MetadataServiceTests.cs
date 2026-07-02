using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WISE.Application.Services;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using Xunit;

namespace WISE.Tests.Application.Services;

/// <summary>
/// P2: 早期終了条件が「成功した Provider の ProvidableFields」から導出されることを検証する。
/// 識別子文字列（FC2 等）による分岐を持たずに、FC2 特性が Provider 宣言で表現できることを確認する。
/// </summary>
public class MetadataServiceTests
{
    // --- テスト用フェイク Provider ---
    private sealed class FakeProvider : IMetadataProvider
    {
        private readonly IReadOnlyList<MetadataCandidate> _candidates;
        private readonly bool _success;

        private readonly Func<string, bool>? _canHandle;

        public FakeProvider(
            string id, int priority,
            IReadOnlyList<MetadataCandidate>? candidates = null,
            bool success = true,
            IReadOnlySet<string>? providableFields = null,
            Func<string, bool>? canHandle = null)
        {
            ProviderId = id;
            Priority = priority;
            _candidates = candidates ?? Array.Empty<MetadataCandidate>();
            _success = success;
            ProvidableFields = providableFields;
            _canHandle = canHandle;
        }

        public string ProviderId { get; }
        public int Priority { get; }
        public IReadOnlySet<string>? ProvidableFields { get; }
        public bool CanHandle(string identifier) => _canHandle?.Invoke(identifier) ?? true;

        public Task<MetadataResult> FetchAsync(MetadataProviderContext context)
            => Task.FromResult(_success
                ? MetadataResult.Succeeded(ProviderId, _candidates, TimeSpan.Zero)
                : MetadataResult.Failed(ProviderId, FailureReason.NotFound, "not found", TimeSpan.Zero));
    }

    private static MetadataCandidate Cand(string provider, string field, int priority)
        => new(provider, field, $"{field}-value", 80, priority);

    private static MetadataProviderContext Context(string identifier, MediaType mediaType = MediaType.Video)
        => new(Guid.NewGuid(), identifier, Array.Empty<WISE.Domain.Entities.MetadataField>(),
               "ja", CancellationToken.None, mediaType);

    private static MetadataService Service(params IMetadataProvider[] providers)
        => new(providers, NullLogger<MetadataService>.Instance);

    // 注: テキスト早期終了(Phase1)後も、カバーが未取得なら Phase2 が後続 Tier を走らせる
    // （カバーが見つかった時点で停止）。そのため「下位 Tier をスキップした」ことを観測するには、
    // 早期終了直後の Tier にカバーを置き、その先の Tier が走らないことを確認する。
    // 以下の A/B は同一トポロジ（3 Tier）で Tier1 の ProvidableFields だけを変え、
    // FC2 特性が識別子分岐なしに終了タイミングを変えることを対照的に示す。

    [Fact]
    public async Task Fc2Provider_TitleOnlyProvidable_StopsBeforeLowestTier()
    {
        // Tier1=Title(Fc2, providable={Title}) / Tier2=Cover / Tier3=Actress
        // Title だけで満足 → Phase2 が Tier2 のカバーを得て停止 → Tier3(Actress)は走らない
        var tier1 = new FakeProvider("Tier1", 60,
            candidates: new[] { Cand("Tier1", "Title", 60) },
            providableFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title" });
        var tier2 = new FakeProvider("Tier2Cover", 50,
            candidates: new[] { Cand("Tier2Cover", "PortraitCover", 50) });
        var tier3 = new FakeProvider("Tier3", 30,
            candidates: new[] { Cand("Tier3", "Actress", 30) });

        var results = (await Service(tier1, tier2, tier3).CollectResultsAsync(Context("FC2-PPV-123"))).ToList();

        results.Should().Contain(r => r.ProviderName == "Tier1");
        results.Should().Contain(r => r.ProviderName == "Tier2Cover");
        results.Should().NotContain(r => r.ProviderName == "Tier3");
    }

    [Fact]
    public async Task NormalVideo_UnconstrainedProvider_RunsLowestTierForActress()
    {
        // 上と同一トポロジだが Tier1 は制約なし(null)。
        // Title だけでは満足せず(Actress/Maker 待ち) → Phase1 が Tier3 まで走る → Tier3 も実行される
        var tier1 = new FakeProvider("Tier1", 60,
            candidates: new[] { Cand("Tier1", "Title", 60) });
        var tier2 = new FakeProvider("Tier2Cover", 50,
            candidates: new[] { Cand("Tier2Cover", "PortraitCover", 50) });
        var tier3 = new FakeProvider("Tier3", 30,
            candidates: new[] { Cand("Tier3", "Actress", 30) });

        var results = (await Service(tier1, tier2, tier3).CollectResultsAsync(Context("ABC-123"))).ToList();

        results.Should().Contain(r => r.ProviderName == "Tier3");
    }

    [Fact]
    public async Task Comic_SatisfiedByTitleAuthorCircle_StopsBeforeLowestTier()
    {
        // Comic: Desired=[Title,Author,Circle]。Tier1 で全て揃う → Phase2 が Tier2 カバーで停止 → Tier3 は走らない
        var tier1 = new FakeProvider("ComicTier1", 100, candidates: new[]
        {
            Cand("ComicTier1", "Title", 100), Cand("ComicTier1", "Author", 100), Cand("ComicTier1", "Circle", 100),
        });
        var tier2 = new FakeProvider("ComicTier2Cover", 50,
            candidates: new[] { Cand("ComicTier2Cover", "PortraitCover", 50) });
        var tier3 = new FakeProvider("ComicTier3", 30,
            candidates: new[] { Cand("ComicTier3", "Author", 30) });

        var results = (await Service(tier1, tier2, tier3)
            .CollectResultsAsync(Context("RJ123", MediaType.Comic))).ToList();

        results.Should().Contain(r => r.ProviderName == "ComicTier1");
        results.Should().NotContain(r => r.ProviderName == "ComicTier3");
    }

    [Fact]
    public async Task NoSuccessInFirstTier_DoesNotEarlyExit()
    {
        // 回帰防止: 最初の Tier が全て失敗した場合、
        // exitFields が空集合になって「何も取得せず満足」と誤判定してはならない。
        var failing = new FakeProvider("Failing", priority: 60, success: false);
        var tier2 = new FakeProvider("Tier2", priority: 30,
            candidates: new[] { Cand("Tier2", "Title", 30), Cand("Tier2", "Actress", 30), Cand("Tier2", "Maker", 30) });

        var results = (await Service(failing, tier2).CollectResultsAsync(Context("ABC-123"))).ToList();

        // 失敗しても Tier2 まで到達している
        results.Should().Contain(r => r.ProviderName == "Tier2" && r.Success);
    }

    [Fact]
    public async Task Fc2Identifier_UnconstrainedProviderThatCannotHandleFc2_IsExcludedFromEligibility()
    {
        // 回帰再現テスト（Jules QA Sprint30 Prompt D で発見）:
        // FANZA/Mgs/JavBus/JavLibrary は識別子を機械的にURL化する実装のため、CanHandle制限が
        // 無い状態だとFC2識別子でも「たまたま」成功し、無関係なMaker/Actress候補を持ち込んで
        // しまう可能性があった（Priority=80のFANZAが最優先Tierで実行されるため特に深刻）。
        // 成功してしまうと ProvidableFields=null（制約なし）が「成功したProvider」に含まれ、
        // GetExitFields が desiredFields 全体（Title/Actress/Maker）を要求してしまい、
        // FC2 の早期終了が機能しなくなる。
        //
        // 修正: FANZA等の実プロバイダに `CanHandle` = "FC2で始まらない" を追加し、
        // eligibleProviders の時点で除外されるようにした。本テストはその防止線（CanHandle
        // フィルタリングがMetadataService側で正しく効くこと）を確認する。
        var fakeFanzaLikeExcluded = new FakeProvider("FanzaLike", priority: 80,
            candidates: new[] { Cand("FanzaLike", "Actress", 80), Cand("FanzaLike", "Maker", 80) },
            canHandle: id => !id.StartsWith("FC2", StringComparison.OrdinalIgnoreCase));
        var fc2 = new FakeProvider("Fc2", priority: 60,
            candidates: new[] { Cand("Fc2", "Title", 60) },
            providableFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title" },
            canHandle: id => id.StartsWith("FC2", StringComparison.OrdinalIgnoreCase));

        var results = (await Service(fakeFanzaLikeExcluded, fc2).CollectResultsAsync(Context("FC2-PPV-4409072"))).ToList();

        // FanzaLike は CanHandle=false のため一度も実行されない
        results.Should().NotContain(r => r.ProviderName == "FanzaLike");
        // Fc2 は Title のみで満足し、正しく早期終了する（＝結果は Fc2 の1件のみ）
        results.Should().ContainSingle(r => r.ProviderName == "Fc2");
    }

    [Fact]
    public async Task Fc2Identifier_IfUnconstrainedProviderSpuriouslySucceeds_EarlyExitIsSuppressed()
    {
        // 上記テストの対照実験: CanHandle制限を「していない」場合に何が起こるかを明示する
        // （＝実際のFANZA等がCanHandleを実装し忘れた場合の退行を検知するための固定用テスト）。
        // これは「あるべき姿」ではなく「制限しなかった場合の既知の问題」を記録するテスト。
        var fakeFanzaLikeUnrestricted = new FakeProvider("FanzaLike", priority: 80,
            candidates: new[] { Cand("FanzaLike", "Title", 80) }); // canHandle未指定=常にtrue、Actress/Makerは取れない
        var fc2 = new FakeProvider("Fc2", priority: 60,
            candidates: new[] { Cand("Fc2", "Title", 60) },
            providableFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title" });

        var results = (await Service(fakeFanzaLikeUnrestricted, fc2).CollectResultsAsync(Context("FC2-PPV-4409072"))).ToList();

        // CanHandleで除外しない限り、制約なしProviderの成功により早期終了が抑制され、
        // 結果的に全Tierが実行されてしまう（＝これが実プロバイダ側でCanHandleを追加した理由）
        results.Should().Contain(r => r.ProviderName == "FanzaLike");
        results.Should().Contain(r => r.ProviderName == "Fc2");
    }
}
