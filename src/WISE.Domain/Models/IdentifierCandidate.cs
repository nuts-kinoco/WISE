namespace WISE.Domain.Models;

/// <summary>
/// IdentifierParser が FileName から抽出した候補。
/// Score / Evidence の付与は FileNameEvidenceProvider の責務であり、
/// Candidate 自体はスコアを持たない純粋な抽出結果。
/// </summary>
public record IdentifierCandidate
{
    /// <summary>どのパターンにマッチしたか（例: "CommercialVideoPattern"）</summary>
    public string PatternName { get; init; }

    /// <summary>抽出された文字列（例: "EKDV-775"）</summary>
    public string ExtractedValue { get; init; }

    public IdentifierCandidate(string patternName, string extractedValue)
    {
        PatternName = patternName ?? throw new ArgumentNullException(nameof(patternName));
        ExtractedValue = extractedValue ?? throw new ArgumentNullException(nameof(extractedValue));
    }
}
