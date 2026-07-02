using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Infrastructure.Services;

/// <summary>
/// Work の動画アセット横に置かれる metadata.json（userMemo/userFavorite/userRating 等）の
/// 読み書きを共通化するヘルパー。DuplicatesController の重複解決や一覧表示など、
/// 複数箇所から参照されるため Infrastructure 層に配置する。
/// </summary>
public static class WorkMetadataJsonHelper
{
    public static string? GetUserMemo(IEnumerable<Asset> assets)
    {
        var videoAsset = FindVideoAsset(assets);
        if (videoAsset?.FilePath == null) return null;

        var metaJsonPath = Path.Combine(Path.GetDirectoryName(videoAsset.FilePath)!, "metadata.json");
        if (!File.Exists(metaJsonPath)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaJsonPath));
            if (doc.RootElement.TryGetProperty("userMemo", out var memoEl))
                return memoEl.GetString();
        }
        catch { }
        return null;
    }

    public static async Task WriteUserMemoAsync(IEnumerable<Asset> assets, string memo)
    {
        var videoAsset = FindVideoAsset(assets);
        if (videoAsset?.FilePath == null) return;

        var workDir = Path.GetDirectoryName(videoAsset.FilePath)!;
        var metaJsonPath = Path.Combine(workDir, "metadata.json");
        try
        {
            Dictionary<string, object?> meta;
            if (File.Exists(metaJsonPath))
            {
                var raw = await File.ReadAllTextAsync(metaJsonPath);
                meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(raw)
                       ?? new Dictionary<string, object?>();
            }
            else
            {
                meta = new Dictionary<string, object?>();
            }
            meta["userMemo"] = memo;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(metaJsonPath, System.Text.Json.JsonSerializer.Serialize(meta, opts));
        }
        catch { }
    }

    private static Asset? FindVideoAsset(IEnumerable<Asset> assets)
        => assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
}
