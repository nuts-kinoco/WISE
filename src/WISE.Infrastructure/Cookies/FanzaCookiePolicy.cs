using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cookies;

/// <summary>
/// FANZA (DMM) の認証Cookie。
/// 優先順位:
///   1. %APPDATA%\WISE\storageState.json  (Playwright exportまたは手動配置)
///   2. ハードコードされたフォールバック値
/// storageState.json の形式は Playwright の storageState() 出力と互換。
/// </summary>
public class FanzaCookiePolicy : ICookiePolicy
{
    private static readonly string StorageStatePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WISE", "storageState.json");

    private static readonly string[] DmmDomains = [".dmm.co.jp", "dmm.co.jp", "video.dmm.co.jp"];

    public string ProviderId => "Fanza";

    public Dictionary<string, string> GetCookies()
    {
        // storageState.json が存在すればそこからロード
        if (File.Exists(StorageStatePath))
        {
            try
            {
                var cookies = LoadFromStorageState(StorageStatePath);
                if (cookies.Count > 0)
                    return cookies;
            }
            catch
            {
                // パース失敗時はフォールバックへ
            }
        }

        // フォールバック: 直書き
        return new Dictionary<string, string>
        {
            ["age_check_done"] = "1",
            ["ckcy"] = "1",
            ["mc_ts"] = "0",
            ["INT_SESID"] = "A1lRXE9CCQJYQTR6d0cKLV4XDlYIQzdkeGYyE14qCRFYWlhEfX9nbjEmMnp3RwoCX1MOGEFbVwobYS91R19BWFFVWERVCVdVWwIHAR4BVABQSQBQUwJJUg0FUhwAUlYEBwAGXgIJVgdAWBIJCl8TDgAJVgZAPg1UGw8VC1RWCEAmUlJWVgMCAgtQFVwRXkJZUF4WB1A+DVQbDxULVF4QA0BYEVwDCxFEEQYTbFoBE1gWXgVVCEMdV1hHBXctMGA6UCksXlt0EQoRWVgJEQREFw07QApfBEYLUA5XBl1WB1dbVFNTD0IJAFNZQ1dGFV0KBgVACl0PRgtKDlYLR0YJEVhSWlwWWEBuAwUHWl8MUBcAO1sUXAQWEgJWXAFeGU8=",
        };
    }

    /// <summary>
    /// Playwright の storageState.json (または互換形式) から DMM ドメインの Cookie を抽出します。
    /// 形式: { "cookies": [ { "name": "...", "value": "...", "domain": "..." } ] }
    /// </summary>
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
            if (!DmmDomains.Any(dm => domain.EndsWith(dm, StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = cookie.TryGetProperty("name", out var n) ? n.GetString() : null;
            var value = cookie.TryGetProperty("value", out var v) ? v.GetString() : null;

            if (!string.IsNullOrEmpty(name) && value != null)
                result[name] = value;
        }

        return result;
    }
}
