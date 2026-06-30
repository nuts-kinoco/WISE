using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WISE.Domain.Models;

namespace WISE.Domain.Services;

/// <summary>
/// IdentifierParser の責務は「候補抽出のみ」。
/// Identifier の決定・スコア付与は行わない。
/// Scoreは Evidence 側（FileNameEvidenceProvider）の責務。
///
/// Pattern追加はこのクラスのみの変更で完結する。
/// </summary>
public static class IdentifierParser
{
    // 商業AV汎用: EKDV, FTAV, IPX, SONE, SSIS, PRED など [A-Z]{2,6}-\d{2,}
    // ホワイトリスト方式は廃止。任意のプレフィックスを許容する。
    private static readonly Regex CommercialRegex =
        new(@"\b([A-Z]{2,6})-(\d{2,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // FC2 / FC2-PPV (例: FC2-PPV-1234567, FC2-1234567)
    private static readonly Regex Fc2Regex =
        new(@"\b(FC2-PPV|FC2)-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 日付形式 (例: 100115-001, 一本道など)
    private static readonly Regex DateRegex =
        new(@"\b(\d{6})-(\d{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // DLSite 同人 RJ (例: RJ123456, RJ01234567)
    private static readonly Regex RjRegex =
        new(@"\b(RJ\d{6,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // DLSite VJ/BJ (VJ=PCゲーム, BJ=書籍/コミック)
    private static readonly Regex VjBjRegex =
        new(@"\b(VJ\d{6,}|BJ\d{6,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // FANZA同人 (例: d_123456, d123456)
    private static readonly Regex FanzaDoujinRegex =
        new(@"\b(d_?\d{5,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 同人誌標準命名規則: (C107) [サークル名 (作者名)] 題名 (出版社).拡張子
    // 例: (C107) [ジャックとニコルソン (のりパチ)] たんさく費用の稼ぎ方 (瑠璃の宝石).zip
    private static readonly Regex DoujinishiRegex =
        new(@"^\s*\([^)]+\)\s*\[([^\]]+)\]\s*([^([]+)", RegexOptions.Compiled);

    /// <summary>
    /// ファイル名から Identifier 候補を全て抽出して返す。
    /// 決定はしない。複数マッチの場合は全て返す。
    /// </summary>
    public static IReadOnlyList<IdentifierCandidate> ExtractCandidates(string fileName)
    {
        var results = new List<IdentifierCandidate>();

        // VJ/BJ を先に評価（CommercialRegex にも合致するため）
        var vjbjMatch = VjBjRegex.Match(fileName);
        if (vjbjMatch.Success)
        {
            results.Add(new IdentifierCandidate("DLSiteVJBJPattern", vjbjMatch.Value.ToUpper()));
            return results;
        }

        // RJ を次に評価（CommercialRegex にも合致するため）
        var rjMatch = RjRegex.Match(fileName);
        if (rjMatch.Success)
        {
            results.Add(new IdentifierCandidate("RJPattern", rjMatch.Value.ToUpper()));
            return results; // RJ と Commercial の二重マッチを避ける
        }

        // FC2 を次に評価（CommercialRegex より優先）
        // FC2-1234567 は FC2-PPV-1234567 に正規化する
        var fc2Match = Fc2Regex.Match(fileName);
        if (fc2Match.Success)
        {
            var numericId = fc2Match.Groups[2].Value;
            var normalized = $"FC2-PPV-{numericId}";
            results.Add(new IdentifierCandidate("FC2Pattern", normalized));
            return results;
        }

        // 日付形式
        var dateMatch = DateRegex.Match(fileName);
        if (dateMatch.Success)
        {
            results.Add(new IdentifierCandidate("DatePattern", dateMatch.Value));
            return results;
        }

        // FANZA同人 (例: d_123456)
        var fanzaMatch = FanzaDoujinRegex.Match(fileName);
        if (fanzaMatch.Success)
        {
            var normalized = NormalizeFanzaId(fanzaMatch.Value);
            results.Add(new IdentifierCandidate("FanzaDoujinPattern", normalized));
            return results;
        }

        // 同人誌標準命名規則: (Event) [Circle (Author)] Title ...
        // 汎用商業AVパターンより先に評価（商業識別子のない同人ファイル向け）
        var doujinMatch = DoujinishiRegex.Match(fileName);
        if (doujinMatch.Success)
        {
            var circle = doujinMatch.Groups[1].Value.Trim();
            // タイトル部分から拡張子を除去
            var titleRaw = doujinMatch.Groups[2].Value.Trim();
            var dotIdx = titleRaw.LastIndexOf('.');
            var title = dotIdx > 0 ? titleRaw[..dotIdx].Trim() : titleRaw;
            var stableId = "DOUJIN-" + GenerateShortHash(circle + "|" + title);
            results.Add(new IdentifierCandidate("DoujinishiPattern", stableId));
            return results;
        }

        // 汎用商業AV（ホワイトリストなし）
        var commercialMatch = CommercialRegex.Match(fileName);
        if (commercialMatch.Success)
        {
            results.Add(new IdentifierCandidate("CommercialVideoPattern", commercialMatch.Value.ToUpper()));
        }

        return results;
    }

    private static string GenerateShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..8];
    }

    private static string NormalizeFanzaId(string raw)
    {
        if (raw.StartsWith("d_", StringComparison.OrdinalIgnoreCase))
            return raw.ToLower();
        return "d_" + raw[1..].ToLower();
    }
}
