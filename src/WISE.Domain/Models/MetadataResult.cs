using System;
using System.Collections.Generic;
using WISE.Domain.Enums;

namespace WISE.Domain.Models;

/// <summary>
/// IMetadataProvider.FetchAsync の戻り値。
/// 成功・失敗・診断情報をすべて含みます。
/// </summary>
public record MetadataResult
{
    public bool Success { get; init; }
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>取得戦略。"Http" | "Browser" | "Cache" など</summary>
    public string Strategy { get; init; } = "Http";

    public FailureReason? FailureReason { get; init; }
    public string? FailureMessage { get; init; }
    public IReadOnlyList<MetadataCandidate> Candidates { get; init; } = Array.Empty<MetadataCandidate>();
    public TimeSpan Elapsed { get; init; }
    public Exception? Exception { get; init; }

    // ファクトリメソッド
    public static MetadataResult Succeeded(
        string providerName,
        IReadOnlyList<MetadataCandidate> candidates,
        TimeSpan elapsed,
        string strategy = "Http") => new()
    {
        Success = true,
        ProviderName = providerName,
        Strategy = strategy,
        Candidates = candidates,
        Elapsed = elapsed
    };

    public static MetadataResult Failed(
        string providerName,
        Enums.FailureReason reason,
        string message,
        TimeSpan elapsed,
        string strategy = "Http",
        Exception? exception = null) => new()
    {
        Success = false,
        ProviderName = providerName,
        Strategy = strategy,
        FailureReason = reason,
        FailureMessage = message,
        Candidates = Array.Empty<MetadataCandidate>(),
        Elapsed = elapsed,
        Exception = exception
    };
}
