# P4: 設計ドキュメント ↔ 実装 乖離監査レポート

> Deep Review Sprint 30 — Prompt 4 成果物。**監査のみ**（実装・ドキュメントの書き換えは未実施）。
> 乖離種別: 📜=ドキュメントが古い/先行しすぎ ／ ⚠=実装が設計から逸脱 ／ 🤔=設計判断が必要

## 1. Constitution 3原則との整合

### Metadata First — 概ね体現 ✅
メタデータ取得パイプライン（Tier制・二段階収集・Conflict Resolver）が機能の中心にあり、トリアージ/再スキャン等のUIも「メタデータを揃えること」を第一級操作として扱っている。原則との重大な乖離なし。

### Work First — 概ね体現、例外1件 🤔
全エンティティがWorkを中心に関連付く構造は維持。ただし **userMemo/userFavorite/userRating が `metadata.json`（ファイルシステム側）にも二重書き込み**されている（`WorksController.cs:240-271`, `DuplicatesController.cs:244-271`）。`Architecture.md §1.2`「DBがすべての判断基準。ファイルシステムはDBが指す先に過ぎない」に照らすと、**ファイル側を正データの一部として読み出している**（`GetUserMemo` はDBでなく metadata.json から読む）のは Source of Truth 原則からの逸脱。ポータビリティ目的の意図的設計の可能性もあるため**設計判断が必要**。

### 引き算の美学 — 体現 ✅
MediaDisplayProfile・ビューワーDI・カバーStrategy Chainは設計どおり実装されており、UI側のMediaType分岐排除も概ね守られている。

## 2. Architecture.md との乖離

| # | 種別 | ドキュメント記述 | 実装の現実 | 推奨アクション |
|---|---|---|---|---|
| D-1 | ⚠ | §2.1 レイヤー図: Controllers→Application→Domain→Infrastructure | 11コントローラーが `WiseDbContext`（Infrastructure）を直接注入 | 実装側を是正（**P1レビュー**で計画済み） |
| D-2 | ⚠ | §5.2 禁止事項「Application層にMediaType分岐を書かない」 | `MetadataService.cs:25-26` に `mediaType == MediaType.Comic/Book` 分岐 | 実装側を是正（**P2レビュー**で計画済み） |
| D-3 | 📜 | §4.1「AssetRole導入（AssetTypeを廃止）」§9変更一覧にも明記 | `AssetRole.cs` と `AssetType.cs` が**両方存在**し、コントローラー層は主に `AssetType` を使用（`AssetsController`, `WorksController:338-342` 等） | 🤔 二重管理は事故のもと。どちらへ収斂するか設計判断→残す方にドキュメントを合わせる |
| D-4 | 📜 | §4.9 FTS5全文検索「検索対象: 全MetadataField」 | FTS5テーブル自体は存在（`Program.cs:220` で `METADATA_FIELD_FTS` 作成、`RebuildFts` ジョブあり）だが、**検索API（`WorksController.GetWorks:78-88`）はFTSを使わずLIKE×8フィールドの相関サブクエリ** | 🤔 検索をFTS5に接続するか、FTS5を廃止してLIKE方針をドキュメント化するか。中途半端な現状が最悪（インデックス更新コストだけ払って使っていない） |
| D-5 | 📜 | §4.10 Collection: 8種の CollectionType（Author/Circle/Person/Series/Maker/Favorite/Playlist/SmartFolder） | `Collection.cs:6-15` は Name/Description のみ。**Typeという概念が実装に存在しない**（実装は手動Playlist相当のみ） | ドキュメント側に「v2時点ではPlaylist相当のみ実装、Typeは将来拡張」と注記 |
| D-6 | 📜 | §4.5 `IMediaViewer`: `ViewerCapabilities(SupportsResume, SupportsPageNavigation, SupportsZoom, SupportsPlaybackSpeed, SupportsBookmark)` | 実装は `SupportsPageNavigation/SupportsDoublePage/SupportsPrefetch/SupportsTimeSeek/SupportsResume`（`WorksController.cs:742-749`）— **フィールド集合が別物** | ドキュメントを実装に合わせて更新 |
| D-7 | 📜 | §4.8 JobType一覧: `MetadataFetch/HashCalc/Thumbnail/CoverExtract/ArchiveIndex/MediaInfo/IndexUpdate/Duplicate` | 実際に生成されるJobType文字列: `FetchMetadata/Import/RebuildFts/System/WatchFolder`（コード全体grep） | ドキュメントを実装に合わせて更新（名称も集合も不一致） |
| D-8 | 📜 | §6.1 「Next.js 15」 | `package.json`: next **16.2.9** / React 19.2.4 | ドキュメント更新（軽微） |
| D-9 | 📜 | §2.2 Work中心図に「Evidence」エンティティ | `src/WISE.Domain/Entities/` に Evidence エンティティは**存在しない**（`IEvidenceProvider` インターフェースと `ProviderDiagnostic` が近縁だが別物） | 🤔 Evidence永続化（§4.3「EvidenceをDBに保存しDiagnostic画面で可視化」）は未実装。Roadmap送りかドキュメント削除かの判断 |

## 3. API.md との乖離（主要なもの）

| # | 種別 | API.md | 実装 |
|---|---|---|---|
| A-1 | 📜 | `POST /api/works/{id}/refresh-metadata` (§2.7) | 実装は `POST /api/jobs/fetchmetadata`（Body に WorkId）+ `/api/jobs/fetchmetadata/batch` |
| A-2 | 📜 | `GET /api/assets/{id}/stream` (§3.2) / `GET /api/assets/{id}/thumbnail` (§3.3) | 実装は `GET /api/assets/{id}/content` に統合。thumbnail 専用エンドポイントなし |
| A-3 | ⚠🤔 | `DELETE /api/works/{id}` は **Soft Delete** (§2.5) | 実装は**物理削除**（DB行削除＋オプションでファイル削除、`WorksController.cs:614-687`）。`Architecture.md §1.2`「ファイルが消えてもWorkは消えない」との整合も要検討 |
| A-4 | 📜 | `GET /api/works/{id}/evidences` (§2.8) | エンドポイント自体が存在しない（D-9と同根） |
| A-5 | 📜 | 記載なし | 実装のみに存在: `/api/duplicates`, `/api/collections`, `/api/home`, `/api/settings`, `/api/works/{id}/user-data`, `/api/works/{id}/metadata`, `/api/works/{id}/related`, `/api/works/{id}/user-tags` 等、**Sprint 20以降の追加APIが軒並み未記載** |

## 4. Metadata.md / Pipeline.md との乖離

| # | 種別 | 内容 |
|---|---|---|
| M-1 | 📜 | HANDOFF.md に記録済みの「フィールド名の大小文字不一致の罠」（Comic系Providerは `author`/`circle` 小文字、Video系は `Actress` 大文字保存。クライアントは両対応が必要）が **Metadata.md / Domain.md のどこにも明文化されていない**。回帰バグの温床として最優先で追記すべき |
| M-2 | 📜 | カバー品質しきい値 125KB（`FetchMetadataJobUseCase.cs:175`）と二段階収集（Phase1テキスト早期終了/Phase2カバーフォールバック）が Pipeline.md に反映されていない（Tier制の記述は概ね整合） |
| M-3 | 📜 | Manual Override は「常にPriority=100」（Metadata.md:240）と記載だが、実装は **confidence 999** を使用（`WorksController.cs:315` 等） |

## 5. 推奨アクション優先度

1. **【高】M-1**: 大小文字不一致の罠を Domain.md/Metadata.md に明文化（ドキュメント修正のみ・即実行可能）
2. **【高】D-4**: FTS5「作ったが使っていない」状態の解消方針を決定（設計判断）
3. **【中】A-3 + Work First逸脱**: 物理削除 vs Soft Delete、metadata.json二重書き込みの扱い（設計判断・関連するので同時に）
4. **【中】D-3**: AssetRole/AssetType の収斂方針決定（設計判断）
5. **【中】API.md全面改訂**: A-1/A-2/A-4/A-5（ドキュメント修正・機械的だが分量大）
6. **【低】D-5/D-6/D-7/D-8/M-2/M-3**: ドキュメント側の追随修正（まとめて1PR）
