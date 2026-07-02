using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Data.Queries;
using Xunit;

namespace WISE.Tests.Infrastructure.Data.Queries;

/// <summary>
/// P3是正(B-3)の統合テスト。GetListAsync/GetRelatedAsync が filtered Include に
/// 変更された後も、WorkItemMapper.Map が参照する全フィールド（Title/Actress/ActressTag/
/// Maker/Label/ReleaseDate/Author/Circle/PageCount/Language/PortraitCover/LandscapeCover）
/// と対応するAsset種別が欠落なくロードされることを検証する。EF Core InMemoryプロバイダは
/// filtered Include の実SQL変換を検証しないため、実際のSqliteプロバイダを使う。
/// </summary>
public class WorksQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WiseDbContext> _options;

    public WorksQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<WiseDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new WiseDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static MetadataField Field(Guid workId, string name, string value, bool isPrimary = true, int confidence = 80)
    {
        var f = new MetadataField(name, value, "TestProvider", isPrimary, confidence);
        f.SetWorkId(workId);
        return f;
    }

    [Fact]
    public async Task GetListAsync_ShouldIncludeAllFieldsUsedByWorkItemMapper()
    {
        var work = new Work("MAP-001");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.Add(work);
            context.MetadataFields.AddRange(
                Field(work.Id, "Title", "テストタイトル"),
                Field(work.Id, "ActressTag", "女優A"),
                Field(work.Id, "Maker", "メーカーA"),
                Field(work.Id, "Label", "レーベルA"),
                Field(work.Id, "ReleaseDate", "2026-01-01"),
                Field(work.Id, "PortraitCover", "/api/assets/dummy/content"),
                Field(work.Id, "LandscapeCover", "/api/assets/dummy2/content"),
                // マッパーが参照しないフィールド（除外されるべき）
                Field(work.Id, "Genre", "ジャンルA"),
                Field(work.Id, "UserTag", "ユーザータグA"),
                Field(work.Id, "SampleImage", "sample1.jpg")
            );
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new WorksQueryService(context);
            var (works, total) = await service.GetListAsync(1, 50, null, null, null, null);

            total.Should().Be(1);
            var loaded = works.Single();

            // マッパーが使うフィールドは全て残っている
            loaded.MetadataFields.Select(f => f.FieldName).Should().Contain(new[]
                { "Title", "ActressTag", "Maker", "Label", "ReleaseDate", "PortraitCover", "LandscapeCover" });

            // マッパーが使わないフィールドは除外されている
            loaded.MetadataFields.Select(f => f.FieldName).Should().NotContain(new[]
                { "Genre", "UserTag", "SampleImage" });
        }
    }

    [Fact]
    public async Task GetListAsync_ShouldIncludeOnlyCoverAssetTypes()
    {
        var work = new Work("MAP-002");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.Add(work);
            work.AddAsset(new Asset("/video.mp4", "video.mp4", 1000, assetType: AssetType.Video));
            work.AddAsset(new Asset("/cover_p.jpg", "cover_p.jpg", 100, assetType: AssetType.PortraitCover));
            work.AddAsset(new Asset("/cover_l.jpg", "cover_l.jpg", 100, assetType: AssetType.LandscapeCover));
            work.AddAsset(new Asset("/thumb.jpg", "thumb.jpg", 50, assetType: AssetType.Thumbnail));
            work.AddAsset(new Asset("/sample.jpg", "sample.jpg", 50, assetType: AssetType.SampleImage));
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new WorksQueryService(context);
            var (works, _) = await service.GetListAsync(1, 50, null, null, null, null);

            var loaded = works.Single();
            loaded.Assets.Select(a => a.AssetType).Should().BeEquivalentTo(
                new[] { AssetType.PortraitCover, AssetType.LandscapeCover });
        }
    }

    [Fact]
    public async Task GetListAsync_SearchByGenre_StillWorks_DespiteFilteredInclude()
    {
        // filtered IncludeはWHERE句（検索条件）に影響しないことを確認する
        // （Genreはマッパー表示には不要だが、検索対象フィールドとしては引き続き機能する必要がある）
        var work = new Work("MAP-003");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.Add(work);
            context.MetadataFields.Add(Field(work.Id, "Genre", "特殊ジャンルXYZ"));
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new WorksQueryService(context);
            var (works, total) = await service.GetListAsync(1, 50, "特殊ジャンルXYZ", null, null, null);

            total.Should().Be(1);
            works.Should().ContainSingle(w => w.Id == work.Id);
        }
    }

    [Fact]
    public async Task GetRelatedAsync_ShouldIncludeAllFieldsUsedByWorkItemMapper()
    {
        var source = new Work("REL-SRC");
        var related = new Work("REL-001");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.AddRange(source, related);
            context.MetadataFields.AddRange(
                Field(source.Id, "Maker", "共通メーカー"),
                Field(related.Id, "Maker", "共通メーカー"),
                Field(related.Id, "Title", "関連作品タイトル"),
                Field(related.Id, "Genre", "除外されるべきジャンル")
            );
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new WorksQueryService(context);
            var results = await service.GetRelatedAsync(source.Id, "Maker", 8);

            results.Should().ContainSingle(w => w.Id == related.Id);
            var loaded = results.Single();
            loaded.MetadataFields.Select(f => f.FieldName).Should().Contain(new[] { "Title", "Maker" });
            loaded.MetadataFields.Select(f => f.FieldName).Should().NotContain("Genre");
        }
    }
}
