using System.Threading.Tasks;
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
    /// Metadataを取得します。
    /// 失敗時も例外をスローせず MetadataResult.Failed(...) を返します。
    /// </summary>
    Task<MetadataResult> FetchAsync(MetadataProviderContext context);
}
