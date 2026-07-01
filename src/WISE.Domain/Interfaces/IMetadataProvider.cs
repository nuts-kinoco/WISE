using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Enums;
using WISE.Domain.Models;

namespace WISE.Domain.Interfaces;

/// <summary>
/// Metadataプロバイダの共通インターフェース。
/// 各実装は MetadataResult を返します（成功・失敗・診断情報を含む）。
/// </summary>
public interface IMetadataProvider
{
    string ProviderId { get; }
    int Priority { get; }

    /// <summary>
    /// このプロバイダが対応する MediaType セット。
    /// null を返す実装は「全 MediaType に対応」を意味する。
    /// </summary>
    IReadOnlySet<MediaType>? SupportedMediaTypes => null;

    /// <summary>
    /// Metadataを取得します。
    /// 失敗時も例外をスローせず MetadataResult.Failed(...) を返します。
    /// </summary>
    Task<MetadataResult> FetchAsync(MetadataProviderContext context);
}
