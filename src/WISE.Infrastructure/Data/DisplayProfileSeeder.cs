using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Infrastructure.Data;

public static class DisplayProfileSeeder
{
    public static async Task SeedAsync(WiseDbContext db)
    {
        foreach (var (mediaType, orientation, fields) in DefaultProfiles())
        {
            if (await db.DisplayProfiles.AnyAsync(p => p.MediaType == mediaType))
                continue;

            var profile = new DisplayProfile(mediaType, orientation, "created_at DESC");
            foreach (var (name, label, visible, order) in fields)
                profile.AddField(new DisplayProfileField(profile.Id, name, label, visible, order));

            db.DisplayProfiles.Add(profile);
        }

        await db.SaveChangesAsync();
    }

    private static (MediaType, string, (string, string, bool, int)[])[] DefaultProfiles() =>
    [
        (MediaType.Video, "portrait",
        [
            ("title",        "タイトル",   true,  1),
            ("actress",      "女優",       true,  2),
            ("maker",        "メーカー",   true,  3),
            ("series",       "シリーズ",   false, 4),
            ("release_date", "発売日",     false, 5),
            ("duration",     "収録時間",   false, 6),
            ("rating",       "評価",       false, 7),
        ]),
        (MediaType.Comic, "portrait",
        [
            ("title",        "タイトル",   true,  1),
            ("author",       "作者",       true,  2),
            ("circle",       "サークル",   true,  3),
            ("page_count",   "ページ数",   false, 4),
            ("language",     "言語",       false, 5),
            ("release_date", "発売日",     false, 6),
            ("rating",       "評価",       false, 7),
        ]),
        (MediaType.Book, "portrait",
        [
            ("title",        "タイトル",   true,  1),
            ("author",       "著者",       true,  2),
            ("publisher",    "出版社",     false, 3),
            ("page_count",   "ページ数",   false, 4),
            ("release_date", "発売日",     false, 5),
            ("isbn",         "ISBN",       false, 6),
        ]),
        (MediaType.PhotoBook, "portrait",
        [
            ("title",        "タイトル",   true,  1),
            ("actress",      "モデル",     true,  2),
            ("maker",        "出版社",     false, 3),
            ("release_date", "発売日",     false, 4),
            ("page_count",   "ページ数",   false, 5),
        ]),
        (MediaType.ImageCollection, "landscape",
        [
            ("title",        "タイトル",   true,  1),
            ("author",       "作者",       true,  2),
            ("tags",         "タグ",       false, 3),
        ]),
        (MediaType.Audio, "portrait",
        [
            ("title",        "タイトル",   true,  1),
            ("actress",      "声優",       true,  2),
            ("circle",       "サークル",   false, 3),
            ("duration",     "再生時間",   false, 4),
            ("release_date", "発売日",     false, 5),
        ]),
    ];
}
