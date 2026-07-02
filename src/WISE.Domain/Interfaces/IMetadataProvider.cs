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
    /// 識別子に対応するかどうかを返す。
    /// false を返すと MetadataService がスキップする。
    /// デフォルトは全識別子に対応。
    /// </summary>
    bool CanHandle(string identifier) => true;

    /// <summary>
    /// このProviderが「早期終了の判定」において、確実に供給できるとみなすテキストフィールド名。
    /// null（デフォルト）は「制約なし＝要求されうる全フィールドを供給可能」を意味する。
    ///
    /// これは実際の取得結果を制限するものではなく、MetadataService の早期終了ロジックが
    /// 「取得を待つ意味のあるフィールド」を絞り込むためだけに使う宣言である。
    /// 例: FC2 コンテンツは構造的に女優(Actress)を持たず、販売者(Maker)も欠落しがちなため、
    /// Fc2 系 Provider は Title のみを「確実に供給可能」と宣言する。これにより
    /// 「Actress が永遠に揃わず全Tierを走査してしまう」無駄を、識別子文字列の分岐なしで防ぐ。
    /// </summary>
    IReadOnlySet<string>? ProvidableFields => null;

    /// <summary>
    /// Metadataを取得します。
    /// 失敗時も例外をスローせず MetadataResult.Failed(...) を返します。
    /// </summary>
    Task<MetadataResult> FetchAsync(MetadataProviderContext context);
}
