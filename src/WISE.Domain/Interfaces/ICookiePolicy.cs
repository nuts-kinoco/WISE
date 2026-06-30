using System.Collections.Generic;

namespace WISE.Domain.Interfaces;

/// <summary>
/// 特定サービスへのHTTPリクエストに付与するCookieを生成します。
/// Providerはこのインターフェースのみに依存し、Cookie詳細を直接知りません（OCP準拠）。
/// </summary>
public interface ICookiePolicy
{
    /// <summary>このPolicyが対象とするProviderId</summary>
    string ProviderId { get; }

    /// <summary>付与すべきCookie名 → Cookie値 の辞書を返します</summary>
    Dictionary<string, string> GetCookies();

    /// <summary>Cookie文字列形式（"key=value; key2=value2"）で返します</summary>
    string GetCookieHeader()
    {
        var cookies = GetCookies();
        return string.Join("; ", System.Linq.Enumerable.Select(cookies, kv => $"{kv.Key}={kv.Value}"));
    }
}
