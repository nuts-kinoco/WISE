using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Data.Queries;
using Xunit;

namespace WISE.Tests.Infrastructure.Data.Queries;

/// <summary>
/// P3是正(B-1)の統合テスト。EF Core InMemory プロバイダは実際のSQL変換を検証しないため、
/// 本テストは実際の Sqlite プロバイダ（in-memory Sqlite DB、接続を維持することで永続化）を使う。
/// これにより GroupBy(w => w.PrimaryIdentifier.ToUpper()) や Contains(list) が
/// 実際にSQLへ変換可能であることを検証する。
/// </summary>
public class DuplicatesQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WiseDbContext> _options;

    public DuplicatesQueryServiceTests()
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

    [Fact]
    public async Task GetDuplicateGroupsAsync_ShouldDetectIdentifierDuplicates()
    {
        var workA = new Work("ABC-123");
        var workB = new Work("abc-123"); // 大小文字違いでも重複扱い
        var workC = new Work("XYZ-999"); // 重複なし

        using (var context = new WiseDbContext(_options))
        {
            context.Works.AddRange(workA, workB, workC);
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new DuplicatesQueryService(context);
            var groups = await service.GetDuplicateGroupsAsync();

            groups.Should().ContainSingle(g => g.DetectionType == "identifier");
            var group = groups.Single(g => g.DetectionType == "identifier");
            group.Works.Should().HaveCount(2);
            group.Works.Select(w => w.Id).Should().BeEquivalentTo(new[] { workA.Id, workB.Id });
        }
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_ShouldDetectTitleDuplicates_WhenNoIdentifierDuplicate()
    {
        var workA = new Work("TTL-001");
        var workB = new Work("TTL-002");
        var title = "同一タイトルのテスト作品です"; // 8文字以上

        using (var context = new WiseDbContext(_options))
        {
            context.Works.AddRange(workA, workB);
            var fieldA = new MetadataField("Title", title, "TestProvider", true, 80);
            fieldA.SetWorkId(workA.Id);
            var fieldB = new MetadataField("Title", title, "TestProvider", true, 80);
            fieldB.SetWorkId(workB.Id);
            context.MetadataFields.AddRange(fieldA, fieldB);
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new DuplicatesQueryService(context);
            var groups = await service.GetDuplicateGroupsAsync();

            groups.Should().ContainSingle(g => g.DetectionType == "title");
            var group = groups.Single(g => g.DetectionType == "title");
            group.Works.Should().HaveCount(2);
            group.Works.Select(w => w.Id).Should().BeEquivalentTo(new[] { workA.Id, workB.Id });
        }
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_ShouldExcludeIdentifierDuplicatesFromTitleDetection()
    {
        // 品番が重複しているWorkはタイトル重複判定の対象から除外される
        // （品番重複として既に検出済みのため二重計上しない）
        var workA = new Work("DUP-001");
        var workB = new Work("dup-001");
        var title = "重複除外確認用の長いタイトル文字列";

        using (var context = new WiseDbContext(_options))
        {
            context.Works.AddRange(workA, workB);
            var fieldA = new MetadataField("Title", title, "TestProvider", true, 80);
            fieldA.SetWorkId(workA.Id);
            var fieldB = new MetadataField("Title", title, "TestProvider", true, 80);
            fieldB.SetWorkId(workB.Id);
            context.MetadataFields.AddRange(fieldA, fieldB);
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new DuplicatesQueryService(context);
            var groups = await service.GetDuplicateGroupsAsync();

            groups.Should().ContainSingle(); // identifierグループのみ、titleグループは重複しない
            groups.Single().DetectionType.Should().Be("identifier");
        }
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_ShouldReturnEmpty_WhenNoDuplicates()
    {
        var workA = new Work("SOLO-001");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.Add(workA);
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new DuplicatesQueryService(context);
            var groups = await service.GetDuplicateGroupsAsync();

            groups.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_ShouldNotTreatShortTitles_AsDuplicates()
    {
        // NormalizeTitle後の長さが8文字未満は重複判定の対象外
        var workA = new Work("SHORT-001");
        var workB = new Work("SHORT-002");

        using (var context = new WiseDbContext(_options))
        {
            context.Works.AddRange(workA, workB);
            var fieldA = new MetadataField("Title", "短い", "TestProvider", true, 80);
            fieldA.SetWorkId(workA.Id);
            var fieldB = new MetadataField("Title", "短い", "TestProvider", true, 80);
            fieldB.SetWorkId(workB.Id);
            context.MetadataFields.AddRange(fieldA, fieldB);
            await context.SaveChangesAsync();
        }

        using (var context = new WiseDbContext(_options))
        {
            var service = new DuplicatesQueryService(context);
            var groups = await service.GetDuplicateGroupsAsync();

            groups.Should().BeEmpty();
        }
    }
}
