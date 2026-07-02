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

        public FakeProvider(
            string id, int priority,
            IReadOnlyList<MetadataCandidate>? candidates = null,
            bool success = true,
            IReadOnlySet<string>? providableFields = null)
        {
            ProviderId = id;
            Priority = priority;
            _candidates = candidates ?? Array.Empty<MetadataCandidate>();
            _success = success;
            ProvidableFields = providableFields;
        }

        public string ProviderId { get; }
        public int Priority { get; }
        public IReadOnlySet<string>? ProvidableFields { get; }

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
}
