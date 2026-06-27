using System.Collections.Generic;
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

    // 同人 (RJ形式: RJ123456)
    private static readonly Regex RjRegex =
        new(@"\b(RJ\d{6,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// ファイル名から Identifier 候補を全て抽出して返す。
    /// 決定はしない。複数マッチの場合は全て返す。
    /// </summary>
    public static IReadOnlyList<IdentifierCandidate> ExtractCandidates(string fileName)
    {
        var results = new List<IdentifierCandidate>();

        // RJ を先に評価（CommercialRegex にも合致するため）
        var rjMatch = RjRegex.Match(fileName);
        if (rjMatch.Success)
        {
            results.Add(new IdentifierCandidate("RJPattern", rjMatch.Value.ToUpper()));
            return results; // RJ と Commercial の二重マッチを避ける
        }

        // FC2 を次に評価（CommercialRegex より優先）
        var fc2Match = Fc2Regex.Match(fileName);
        if (fc2Match.Success)
        {
            results.Add(new IdentifierCandidate("FC2Pattern", fc2Match.Value.ToUpper()));
            return results;
        }

        // 日付形式
        var dateMatch = DateRegex.Match(fileName);
        if (dateMatch.Success)
        {
            results.Add(new IdentifierCandidate("DatePattern", dateMatch.Value));
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
}
