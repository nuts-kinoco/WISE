using System;
using System.Collections.Generic;
using System.IO;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cookies;

/// <summary>
/// MGStage の年齢確認Cookie。
/// adc=1 は常に付与。追加のセッションCookieを %APPDATA%\WISE\mgsCookies.txt から読み込む。
/// </summary>
public class MgsCookiePolicy : ICookiePolicy
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WISE");

    private static readonly string CookieTxtPath =
        Path.Combine(AppDataDir, "mgsCookies.txt");

    public string ProviderId => "Mgs";

    public Dictionary<string, string> GetCookies()
    {
        var cookies = new Dictionary<string, string>
        {
            ["adc"] = "1"
        };

        if (!File.Exists(CookieTxtPath)) return cookies;

        try
        {
            var line = File.ReadAllText(CookieTxtPath).Trim();
            if (string.IsNullOrEmpty(line)) return cookies;

            foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eqIdx = part.IndexOf('=');
                if (eqIdx <= 0) continue;
                var name = part[..eqIdx].Trim();
                var value = part[(eqIdx + 1)..].Trim();
                if (!string.IsNullOrEmpty(name))
                    cookies[name] = value;
            }
        }
        catch { /* パース失敗時は adc=1 のみ返す */ }

        return cookies;
    }
}
