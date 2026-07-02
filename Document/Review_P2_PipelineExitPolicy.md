# P2: メタデータ収集終了条件の Provider 特性ベース再設計（提案）

> Deep Review Sprint 30 — Prompt 2 成果物。**実装は承認後**。

## 1. 現状のハードコード分岐（対症療法の一覧）

`MetadataService.GetExitFields()`（`src/WISE.Application/Services/MetadataService.cs:23-35`）:

```csharp
if (mediaType == MediaType.Comic) return ["Title", "Author", "Circle"];
if (mediaType == MediaType.Book)  return ["Title", "Author"];
if (identifier.StartsWith("FC2", ...)) return ["Title"];  // FC2は構造的にMaker/Actressを欠く
return ["Title", "Actress", "Maker"];
```

問題点:
1. **FC2分岐は「識別子プレフィックス」で「Provider群の構造的特性」を代弁している**。FC2以外にも同様の特性を持つ提供元が増えるたびに分岐が増える
2. `mediaType ==` 分岐は `Architecture.md §5.2` の禁止事項「Application層にMediaType分岐を書かない」に**明示的に違反**している（P4監査 D-2 参照）
3. カバー品質しきい値（125KB）は `FetchMetadataJobUseCase.cs:175` にあり、テキスト充足判定と終了ポリシーが2レイヤに分散

## 2. 提案：「期待できるフィールド集合」を宣言から導出する

### 中核アイデア

> **終了条件 = 「このMediaTypeで欲しいフィールド」∩「実行対象Provider群が構造的に供給しうるフィールドの和集合」**

FC2の識別子分岐は消える。FC2系識別子では `CanHandle` により実行対象がFC2系Providerに絞られ、FC2系Providerが「Maker/Actressは供給できない」と宣言していれば、期待集合は自動的に `["Title"]` に縮退する。**個別ケースの知識がProvider自身の宣言に局所化される。**

### 変更内容（最小）

**(1) `IMetadataProvider` にデフォルト付きプロパティを1つ追加**（既存実装は無変更でコンパイル可）:

```csharp
/// <summary>
/// このProviderが構造的に供給しうるフィールド名。null = 制約なし（全フィールド供給可能とみなす）。
/// 終了条件の算出にのみ使用され、実際の取得結果を制限するものではない。
/// </summary>
IReadOnlySet<string>? ProvidableFields => null;
```

**(2) FC2系Provider（`Fc2MetadataProvider` / `Fc2AltMetadataProvider`）のみ宣言をオーバーライド**:

```csharp
public IReadOnlySet<string>? ProvidableFields { get; } =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Title", "PortraitCover", "LandscapeCover", "Seller", "ReleaseDate", "Genre" };
```
（実際の抽出コードを確認して過不足なく列挙する。**Maker/Actressを含めない**ことが本質）

**(3) `MetadataService.GetExitFields` を置き換え**:

```csharp
private static readonly IReadOnlyDictionary<MediaType, string[]> DesiredFields =
    new Dictionary<MediaType, string[]>
    {
        [MediaType.Comic] = ["Title", "Author", "Circle"],
        [MediaType.Book]  = ["Title", "Author"],
        // 既定（Video等）
    };
// 分岐(if)ではなくデータ(辞書)。eligible providers の ProvidableFields 和集合と交差を取る
```

`identifier.StartsWith("FC2")` は削除。

### 退行しないことの根拠（真理値表）

| ケース | eligible providers | 供給可能和集合 | Desired | 交差＝新Exit | 旧Exit | 一致 |
|---|---|---|---|---|---|---|
| FC2-PPV-xxx (Video) | FC2, Fc2Alt のみ | Title, Cover系, … (Maker/Actressなし) | Title, Actress, Maker | **Title** | Title | ✅ |
| 通常AV (Video) | 全Videoプロバイダ | 制約なし(null含む→全部) | Title, Actress, Maker | Title, Actress, Maker | 同左 | ✅ |
| Comic | ComicInfoXml, DLSite等 | 制約なし | Title, Author, Circle | Title, Author, Circle | 同左 | ✅ |
| Book | 同上 | 制約なし | Title, Author | Title, Author | 同左 | ✅ |

## 3. 意図的にやらないこと（引き算）

- **カバー品質しきい値のIMetadataProvider化はしない**。125KBは「取得後の品質判定」でありProvider特性ではない。`FetchMetadataJobUseCase` に残す（コメントで相互参照だけ追加）
- **Tier/Priority機構は触らない**。Priorityグループ並列・二段階収集（Phase1/2）は今回のスコープ外で、現に機能している
- **プラグイン向けの汎用ポリシーエンジンは作らない**。`Plugin.md` の将来像とは「Providerが自分の特性を宣言する」点で整合しており、これ以上の抽象は現時点で不要

## 4. 実装・検証手順（承認後）

1. `IMetadataProvider.ProvidableFields` 追加 → FC2系2Providerに宣言実装 → `MetadataService` 書き換え
2. `dotnet build` + `WISE.Tests`
3. 回帰確認スキャン: FC2系品番1件（Exit=Titleで早期終了すること）/ 通常AV品番1件（HOIZ-017等）/ Comic 1件、それぞれログで `Text fields satisfied at Priority=...` の発火位置が変わらないこと
