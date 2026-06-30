using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cookies;

/// <summary>
/// FC2コンテンツマーケットの年齢確認Cookie。
/// 優先順位:
///   1. %APPDATA%\WISE\fc2StorageState.json  (Playwright export または Cookie Editor エクスポート)
///   2. %APPDATA%\WISE\fc2Cookies.txt        (Cookie ヘッダー文字列を1行で記述)
/// storageState.json の形式は Playwright の storageState() 出力と互換。
/// </summary>
public class Fc2CookiePolicy : ICookiePolicy
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WISE");

    private static readonly string StorageStatePath =
        Path.Combine(AppDataDir, "fc2StorageState.json");

    private static readonly string CookieTxtPath =
        Path.Combine(AppDataDir, "fc2Cookies.txt");

    private static readonly string[] Fc2Domains =
        [".fc2.com", "fc2.com", "adult.contents.fc2.com", "contents.fc2.com"];

    public string ProviderId => "Fc2";

    public Dictionary<string, string> GetCookies()
    {
        // 1. storageState.json (Playwright / Cookie Editor JSON エクスポート)
        if (File.Exists(StorageStatePath))
        {
            try
            {
                var cookies = LoadFromStorageState(StorageStatePath);
                if (cookies.Count > 0)
                    return cookies;
            }
            catch { /* パース失敗時は次へ */ }
        }

        // 2. fc2Cookies.txt (Cookie ヘッダー文字列: "name1=value1; name2=value2")
        if (File.Exists(CookieTxtPath))
        {
            try
            {
                var line = File.ReadAllText(CookieTxtPath).Trim();
                if (!string.IsNullOrEmpty(line))
                    return ParseCookieLine(line);
            }
            catch { /* パース失敗時は空を返す */ }
        }

        return [];
    }

    private static Dictionary<string, string> LoadFromStorageState(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("cookies", out var cookiesArray))
            return [];

        var result = new Dictionary<string, string>();

        foreach (var cookie in cookiesArray.EnumerateArray())
        {
            var domain = cookie.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
            if (!Fc2Domains.Any(dm => domain.Contains(dm, StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = cookie.TryGetProperty("name", out var n) ? n.GetString() : null;
            var value = cookie.TryGetProperty("value", out var v) ? v.GetString() : null;

            if (!string.IsNullOrEmpty(name) && value != null)
                result[name] = value;
        }

        return result;
    }

    private static Dictionary<string, string> ParseCookieLine(string line)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0) continue;
            var name = part[..eqIdx].Trim();
            var value = part[(eqIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(name))
                result[name] = value;
        }
        return result;
    }
}
