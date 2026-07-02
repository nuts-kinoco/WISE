# P1: `WiseDbContext` コントローラー直接注入の是正方針（分析＋提案）

> Deep Review Sprint 30 — Prompt 1 成果物。**実装は承認後**（本書は分析と計画のみ）。

## 1. 現状マッピング

`WiseDbContext` を直接注入しているのは **11コントローラー**。使用パターンで分類すると：

| コントローラー | 行数 | 分類 | 備考 |
|---|---|---|---|
| `WorksController` | 819 | ★複雑クエリ＋書込＋ファイルI/O混在 | 検索/ソート/詳細/手動メタデータ/カバー管理/削除/EPUB配信が同居。最重量 |
| `DuplicatesController` | 273 | ★複雑クエリ＋書込＋ファイルI/O | 全Works読込→メモリ内グルーピング。Resolveはループ内SaveChanges×2（トランザクション境界なし） |
| `JobsController` | 263 | 混成 | importは`CreateImportJobUseCase`経由（正）だがfetchmetadataは直接DbContext（不整合） |
| `CollectionsController` | 226 | 単純CRUD＋中規模クエリ | 2段クエリ（コレクション→カバー解決） |
| `SystemController` | 223 | 読取＋ファイルI/O | EventLogs読取/削除。Cookie/ダイアログ等はDB無関係 |
| `SettingsController` | 167 | 混成 | DisplayProfileは`IDisplayProfileRepository`経由（正）、AppSettingsは直接（不整合が同一ファイル内に共存） |
| `ReaderController` | 152 | 読取のみ | Work+Assets取得→ArchiveReader委譲 |
| `HomeController` | 110 | 読取のみ | ⚠ `Task.WhenAll`で同一DbContextに並行クエリ（P3監査 A-1 参照） |
| `AssetsController` | 114 | 読取のみ | Asset 1件取得→ファイル配信 |
| `WatchFoldersController` | 79 | 単純CRUD | 最小 |
| `HistoryController` | 58 | 読取のみ | 最小 |

**模範例（違反していない2つ）**:
- `ReadingHistoryController` — `IReadingHistoryRepository` のみ注入。**リポジトリパターンの完成形が既にリポジトリ内にある**
- `ImportController` — `ImportUseCase` 経由

## 2. 重要な発見：設計倒れのQueryインターフェース

`src/WISE.Application/Queries/` に以下が**定義のみ・実装ゼロ・DI未登録**で存在する：

- `IGalleryQueryService` / `IWorkDetailQueryService` / `IHistoryQueryService` / `IJobQueryService`
- 付随DTO（`WorkCardDto`, `WorkDetailDto`, `HistoryItemDto`, `JobDto`）

ただし**DTOの形が現行APIレスポンスと乖離**している（例: `WorkCardDto` に Actress/Maker/CoverUrl がなく `HasConflict` という未実装概念がある）。設計初期の遺物であり、そのまま実装しても現行フロントと接続できない。

## 3. 採用案の提案：**案C（読み書き分離の混成）を修正して採用**

| 案 | 評価 |
|---|---|
| 案A: Repository全面展開 | ❌ EAV検索・集計・射影が多く、`IWorkRepository`型の汎用CRUDでは表現できない。「Repositoryにクエリメソッドが乱立」する未来が見える |
| 案B: 全部UseCase/Queryハンドラ | △ 書込系には最適だが、読取専用のためだけに1クエリ=1クラスは過剰（引き算の美学に反する） |
| **案C: 読取=QueryService / 書込=UseCase** | ✅ 既存の`Queries/`ディレクトリの意図を回収しつつ、`ReadingHistoryController`+`ImportController`の確立済みパターンと整合 |

### 案Cの具体形

1. **読取**: `Application/Queries/` のインターフェースを**現行APIレスポンス形に合わせて再定義**し、実装を `Infrastructure/Data/Queries/` に置く。DTOは現行レスポンスから逆算（`WorkItemMapper`のロジックはQueryService実装に移動候補）。
   - 陳腐化した既存DTO（`HasConflict`等）は削除
2. **書込**: `Application/UseCases/`（または `Api/UseCases/` の現行流儀）に移す。ファイルI/O（metadata.json同期、物理削除）はUseCase内でInfrastructureサービスに委譲
3. **例外を明文化**: `AssetsController` のRange Request処理のようなHTTP固有ストリーミングは、Asset解決だけQueryService化し、レスポンス構築はコントローラーに残す

## 4. 段階的実行計画（PR分割）

| Phase | 対象 | 内容 | リスク |
|---|---|---|---|
| **1** | `HistoryController` + `WatchFoldersController` | 最小2本でパターン確立（`IHistoryQueryService`再定義+実装、WatchFolder CRUD UseCase化） | 低 |
| 2 | `HomeController` + `SettingsController` | Home読取のQueryService化（併せてP3監査A-1の並行クエリ問題を構造的に解消）。SettingsのAppSettings直接アクセスをRepository化し同一ファイル内の不整合解消 | 低 |
| 3 | `CollectionsController` + `ReaderController` + `AssetsController` | 中規模。Reader/Assetsは読取専用なので機械的 | 中 |
| 4 | `JobsController` + `DuplicatesController` | Duplicatesは**Resolveのトランザクション境界再設計**（P3監査A-2）を同時に実施 | 中 |
| 5 | `WorksController` | 最後。検索/詳細/手動メタデータ/カバー管理/削除の**責務分割**（4〜5 UseCaseと2 QueryServiceに解体） | 高 |

各Phaseの完了条件: `dotnet build` 成功 + `WISE.Tests` パス + 該当画面のE2E確認。

## 5. 判断が必要な点（承認時に決めてください）

1. **UseCaseの置き場所**: 現状 `Api/UseCases` と `Application/UseCases` に分裂している。**推奨: `Application/UseCases` に統一**（Api層のものはEF/Infrastructure依存があるため要調整。依存が切れないものはまず現状位置のまま命名統一のみ）
2. **陳腐化Queryインターフェースの扱い**: **推奨: 再定義**（削除して作り直し。旧DTOの `HasConflict` 等は捨てる）
3. Phase 1 の着手承認
