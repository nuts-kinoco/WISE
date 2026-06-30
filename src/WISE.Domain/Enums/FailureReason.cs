namespace WISE.Domain.Enums;

/// <summary>
/// Metadata取得失敗の理由を表します。
/// Provider Diagnostics・ログ・将来のBrowserフォールバック判定に使用します。
/// </summary>
public enum FailureReason
{
    /// <summary>品番に該当するコンテンツが存在しない</summary>
    NotFound,

    /// <summary>ネットワークエラー（接続失敗・DNS解決失敗など）</summary>
    Network,

    /// <summary>タイムアウト</summary>
    Timeout,

    /// <summary>HTMLパース失敗（DOM構造変更などで期待する要素が見つからない）</summary>
    ParserError,

    /// <summary>Provider内部エラー（予期しない例外）</summary>
    ProviderError,

    /// <summary>レートリミット・429 Too Many Requests</summary>
    RateLimit,

    /// <summary>年齢確認ページ・Cookieなしでブロックされた</summary>
    AgeVerification,

    /// <summary>未分類の失敗</summary>
    Unknown
}
