# P3: EF Core クエリ翻訳リスク横断監査

> Deep Review Sprint 30 — Prompt 3 成果物。対象: `src/WISE.Api/Controllers/` 全14ファイル + `src/WISE.Infrastructure/Data/`。
> 「修正済み」= 本監査コミットで修正。「要相談」= クエリ/トランザクション再構成が必要で単独判断を避けたもの。

## リスク度: 高

### A-1. 同一DbContextへの並行クエリ（スレッド安全性違反）【修正済み】
- **`HomeController.cs:26-33`** — `Task.WhenAll` で3つの非同期クエリ（ContinueWatching / RecentlyAdded / Favorites）を**同一Scoped DbContextに同時発行**。EF CoreのDbContextはスレッドセーフではなく、公式には `InvalidOperationException`（"A second operation was started..."）となる違反パターン。現在動作しているのは Microsoft.Data.Sqlite の非同期APIが実質同期実行されるという**実装詳細に偶然依存**しているだけで、プロバイダ更新・接続プーリング変更で即座に壊れる。
- **修正**: 逐次 `await` に変更（SQLiteでは実行時間も変わらない）。

### A-2. ループ内 SaveChangesAsync 複数回＋トランザクション境界なし【要相談】
- **`DuplicatesController.cs:164-239` (`Resolve`)** — `DeleteWorkIds` のループ内で作品ごとに `FirstOrDefaultAsync`（N+1）＋ `SaveChangesAsync` を2回ずつ実行。3件目で例外が出ると1-2件目のマージ・削除だけが確定した**部分適用状態**が残る。物理ファイル削除はDB削除後なので孤立はしないが、`WorksController.DeleteWork:632-672` が採っている「ファイル削除失敗ならDBを触らない」方針とも非対称。
- **推奨**: `BeginTransactionAsync` で全体を包み、事前に `Where(w => ids.Contains(w.Id))` で一括ロード。P1計画のPhase 4（Duplicates UseCase化）と同時に実施するのが二度手間がない。

## リスク度: 中

### B-1. 全Works＋子テーブルの全件メモリロード
- **`DuplicatesController.cs:33-37` (`GetDuplicates`)** — `Works.Include(MetadataFields).Include(Assets).ToListAsync()` で**ライブラリ全件**をロード。`NormalizeTitle`（`Regex.Replace`）がSQL翻訳不能なため意図的なクライアント評価だが、無条件Include×2は蔵書1万件規模でメモリ・速度とも顕著に劣化する。
- **判定**: 現規模では動作するため今回は指摘のみ。将来は「識別子重複はSQL側 `GroupBy`、タイトル正規化は `Title` フィールドのみの射影取得後にメモリ処理」の2段構えに分離可能。

### B-2. ループ内 FindAsync（N+1）【修正済み】
- **`JobsController.cs:249-252` (`EnqueueFetchMetadataBatch`)** — workIdごとに `FindAsync`。一括バリデーションに置換可能。
- **修正**: `Where(w => workIds.Contains(w.Id)).Select(w => w.Id)` で存在IDを1クエリ取得に変更。

### B-3. 一覧APIでのフルエンティティ＋二重Include
- **`WorksController.cs:47-51` (`GetWorks`)** — 一覧表示に `MetadataFields`＋`Assets` を全列Includeし、ページサイズ100×子テーブル全列を転送。`WorkItemMapper.Map` がエンティティ全体を要求する構造のため射影で解決するにはMapper再設計が必要。
- **判定**: P1計画のPhase 5（WorksController解体・QueryService化）で射影ベースに置換するのが本筋。今回は指摘のみ。

### B-4. LINQツリー内のカスタムメソッド呼び出し（現状セーフ、地雷予備軍）
- **`DuplicatesController.cs:62-63`** — `NormalizeTitle(...)` を `Select` 内で呼ぶが、直前の `ToListAsync()`（B-1）でメモリに落ちているため翻訳エラーは**発生しない**。ただしB-1を最適化する際にこのSelectをDB側に残すと即座に `InvalidOperationException` になる。B-1対応時の注意点として記録。

## リスク度: 低

### C-1. 読み取り専用クエリの AsNoTracking 欠落【修正済み】
| 箇所 | 内容 |
|---|---|
| `JobsController.cs:35` (`GetJobs`) | 一覧100件を追跡ロード |
| `JobsController.cs:56` (`GetActiveJobs`) | ポーリングで**2秒ごと**に呼ばれる高頻度クエリ。追跡コストが常時発生 |
| `JobsController.cs:72` (identifiers解決) | 射影だが明示しておくのが安全（Selectのみなら実害なし・可読性目的） |
| `HistoryController.cs:37` | `Include(TargetWork)` 付き100件を追跡ロード |
| `WatchFoldersController.cs:24` | 全件追跡ロード |
| `SettingsController.cs:37` | `ToDictionaryAsync` 前に追跡 |
- **修正**: 上記に `AsNoTracking()` を付与（`FindAsync` 使用箇所は更新系のため対象外）。

### C-2. 追跡済みエンティティへの冗長 Update 呼び出し【修正済み】
- **`SettingsController.cs:161`** — `FindAsync` で取得した追跡済み `AppSetting` に `_db.AppSettings.Update(existing)`。全プロパティをModified扱いにするだけで不要。
- **修正**: `Update` 呼び出しを削除（`SetValue` の変更検知で十分）。

### C-3. 射影と併用された無効な Include
- **`SystemController.cs:28`** — `Include(e => e.TargetWork)` の直後に `Select` 射影。EF CoreはSelect時にIncludeを無視するため無害だが誤解を招く。**指摘のみ**（動作に影響なし。P1リファクタ時に除去）。

### C-4. IN句の肥大化リスク（現状は上限があり許容）
- `CollectionsController.cs:44`（コレクション数上限）/ `WorksController.cs:771-786`（関連作品、フィールド6種×値数個）/ `HomeController.cs:82`（8件）— いずれも実質的な件数上限があり問題なし。**指摘のみ**。

## 総括

| 分類 | 件数 | 修正済み | 要相談/指摘のみ |
|---|---|---|---|
| 高 | 2 | 1 (A-1) | 1 (A-2 → P1 Phase4で対応推奨) |
| 中 | 4 | 1 (B-2) | 3 (B-1/B-3はP1リファクタと同時が合理的) |
| 低 | 4 | 2 (C-1, C-2) | 2 (無害) |
