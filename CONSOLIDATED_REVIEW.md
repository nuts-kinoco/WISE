# WISE v2 — Consolidated Review & Sprint Backlog

> 生成日: 2026-07-01  
> ソース: 8レポート（UI/UX×2、DB、Metadata×2、Performance、QA Destructive、Media Collector UX）  
> 判断基準: WISE v2 Product Constitution（Metadata First / Work First / 引き算の美学）

---

## 1. Consolidated Review（重複統合・矛盾整理）

### 1.1 UI / UX

**3つのレポートで一致した指摘（採用確定）:**
- WorkCard: デフォルトで情報を出しすぎ。Normal/Compact はカバーのみ、ホバーでタイトル+Rating表示が正解
- Detail ページ: 技術情報（Assets/Diagnostics/History）がメインビューに露出している
- Primary CTA（Play/Read）がDetailページの英雄エリアに存在しない
- 空のギャラリー: HardDriveUpload アイコン+テキストのみで onboarding 不十分
- Diagnostics の生データ（Confidence%、Evidence、Regex）がユーザーに見える → Developer Mode 専用にすべき

**レポート間の矛盾（判断済み）:**
- "リスト表示に品番列を残す" vs "リスト表示は Spreadsheet 禁止"  
  → Constitution優先: 品番はデフォルト非表示。Display Profile で ON にできる形で残す
- "サイドパネルで Detail を開く" vs "Detail は専用ページ"  
  → 現状維持: 実装工数高。Detail ページを十分に速く・美しくする方向で代替

### 1.2 Database

**採用:**
- EAV テーブル（MetadataFields）に複合インデックス `(FieldName, Value)` がない → フルスキャン確定
- Works テーブルに `Status`, `MediaType`, `Favorite` インデックスがない
- FTS5 は Migration で CREATE は存在するが、インクリメンタル更新が未整備
- `PRAGMA foreign_keys = ON` が接続文字列で担保されているか要確認

**保留（スケールしてから対応）:**
- wise_logs.db 分離（JobLog, EventLog を別ファイルに）
- MergedIntoId に FK 制約追加

### 1.3 Metadata Architecture

**採用:**
- `MetadataService.Tier1ExitFields` が `["Title","Actress","Maker"]` にハードコードされている → MediaType ごとに動的化（Comic/Book は `Author`, `Circle` が本質フィールド）
- Evidence スコアが同一シグナルで重複加算されうる → 同一パターン源の重複抑制（保留: Phase 2）

**保留:**
- EAV に `DataType` 列追加（拡張性はあるが今は過剰）
- 言語タグ列（多言語は Phase 3 以降）

### 1.4 Scraping / Provider

**採用:**
- Retry Policy（Polly）が **実装ゼロ**。全HTTPコールが transient failure で死ぬ
- ドメインごとのレートリミット（Token Bucket）が未実装。429 が来てから止まる設計
- `SupportedMediaTypes` のチェックが緩い（FANZA が Comic に対して発火する可能性）
- JavBus の Cloudflare 対応が `FutureStrategy=Browser` のまま（警告ログで終了）
- HTTPレスポンスキャッシュ未実装（同一URLを何度もスクレイプ）

**中期採用（Phase 2）:**
- JavLibrary プロバイダー追加
- Playwright Cookie 取得の UI ガイド化

**却下:**
- Maker 公式サイト（SOD/S1/Prestige）への個別対応 → 維持コスト高、ROI 低

### 1.5 Performance & Stability

**採用:**
- Gallery の Cover API に `ETag` / `Cache-Control` ヘッダーがない → ブラウザが毎回再取得
- `ImportUseCase.cs`: `Directory.GetFiles(...).ToList()` → `UnauthorizedAccessException` で全件失敗
- 大量ファイルでの eager `.ToList()` → OOM リスク

**保留:**
- ArchiveIndexJob（インポート時にページリストをJSONキャッシュ）→ Phase 2

---

## 2. Priority Matrix

### Critical（次スプリントで必ず対応）

| # | 場所 | 内容 | 根拠 |
|---|------|------|------|
| C1 | `ImportUseCase.cs` | `Directory.GetFiles` → `EnumerateFiles` + UnauthorizedAccess スキップ | Destructive QA: 制限ディレクトリで全件失敗 |
| C2 | `MetadataService.cs` | Tier1ExitFields を MediaType ごとに動的化 | Comic/Book に Actress/Maker チェックが走る |
| C3 | Infrastructure | Polly で全 HTTP 呼び出しに exponential backoff retry | 現状 transient error で全部死ぬ |

### High（Sprint 23〜24）

| # | 場所 | 内容 |
|---|------|------|
| H1 | Migrations | MetadataFields に `(FieldName, Value)` 複合インデックス |
| H2 | Migrations | Works に `(Status)`, `(MediaType)`, `(Favorite)` インデックス |
| H3 | Cover API | `ETag` + `Cache-Control: max-age=86400` ヘッダー |
| H4 | Detail Page | 英雄エリアに Play/Read CTA。Assets は accordion に移動 |
| H5 | WorkCard | Normal/Compact でホバー前はカバーのみ（scrim overlay on hover） |
| H6 | `ImportUseCase.cs` | `.ToList()` を streaming 処理（上限件数制御）に変更 |

### Medium（Sprint 25〜26）

| # | 場所 | 内容 |
|---|------|------|
| M1 | Infrastructure | ドメインごとの Token Bucket レートリミット |
| M2 | `MetadataService` | `SupportedMediaTypes` チェックを厳格化 |
| M3 | GalleryGrid | 空状態を friendly onboarding に変更（CTA + illustration） |
| M4 | Detail Page | Diagnostics を Developer Mode フラグで隠蔽 |
| M5 | GalleryGrid | リスト表示の「品番」列をデフォルト非表示 |
| M6 | FTS5 | 更新をバックグラウンドジョブ化（同期 INSERT が重い） |

### Low（Sprint 27+）

| # | 場所 | 内容 |
|---|------|------|
| L1 | Infrastructure | HTTP レスポンスキャッシュ（SQLite, TTL 付き） |
| L2 | JavBus | Playwright + stealth で Cloudflare 動的回避 |
| L3 | Evidence | 同一パターン源の重複スコア加算を抑制 |
| L4 | Metadata | `altTitle` / `uncensored` フィールド追加 |
| L5 | DB | `PRAGMA foreign_keys = ON` を接続文字列で明示 |

### Nice to Have（Phase 3以降）

- JavLibrary プロバイダー / Bulk Edit / Deduplication Compare View
- Smart Folders（ダイナミックコレクション）/ Playwright Cookie UI

---

## 3. Architecture Review

### 変更不要（現状維持）

- DDD 4層（Domain / Application / Infrastructure / Api）— 全レビュアーが正当と評価
- EAV + MetadataField — 柔軟性優先として維持。ただしインデックスを補強
- Chain of Responsibility（Cover Provider）— 拡張性が高く修正不要
- Provider Plugin 設計（`IMetadataProvider` 追加だけで動く）— 設計通り維持

### 追加が必要（Infrastructure 変更）

- Polly NuGet パッケージ追加 → 全 `HttpClient` に retry policy
- Token Bucket クラス追加（共有サービスとして DI 登録）
- Migration: Works/MetadataFields のインデックス追加
- Migration: `AppSettings.DeveloperMode` キー追加（Diagnostics 表示制御）

### UI のみ変更

- Detail Page 構造変更（ZoneModel: Immersion / Context / Technical / DevOnly）
- WorkCard hover state
- GalleryGrid 空状態

---

## 4. Refactoring Plan

### ImportUseCase.cs — 最優先

```csharp
// After: ストリーミング + アクセス拒否ディレクトリをスキップ
static IEnumerable<string> EnumerateSafe(string root, HashSet<string> extensions)
{
    var queue = new Queue<string>();
    queue.Enqueue(root);
    while (queue.Count > 0)
    {
        var dir = queue.Dequeue();
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch (UnauthorizedAccessException) { continue; }
        catch (IOException) { continue; }
        foreach (var e in entries)
        {
            if (Directory.Exists(e)) queue.Enqueue(e);
            else if (extensions.Contains(Path.GetExtension(e).ToLowerInvariant())) yield return e;
        }
    }
}
```

### MetadataService.cs — Tier1ExitFields 動的化

```csharp
private static HashSet<string> GetTier1ExitFields(MediaType mediaType) => mediaType switch
{
    MediaType.Comic => new() { "Title", "Author", "Circle" },
    MediaType.Book  => new() { "Title", "Author" },
    _               => new() { "Title", "Actress", "Maker" },  // Video / PhotoBook
};
```

### DB Indexes — Migration 追加

```csharp
entity.HasIndex(e => new { e.FieldName, e.Value });  // MetadataFields
entity.HasIndex(e => e.Status);                       // Works
entity.HasIndex(e => e.MediaType);                    // Works
entity.HasIndex(e => e.Favorite);                     // Works
```

---

## 5. UX Improvements

### Detail Page — Zone Model

```
Zone 1 (Immersion):  ヒーローバナー + カバー + タイトル + 評価 + [再生/読む]
Zone 2 (Context):    Synopsis / 出演 / ジャンル / シリーズ / メーカー (chip)
Zone 3 (Technical):  Assets / History ← accordion、デフォルト閉じ
Zone 4 (Dev only):   Diagnostics / Evidence / Raw JSON ← DeveloperMode=true のみ
```

### WorkCard — Hover State

- デフォルト: カバー画像のみ
- ホバー: scrim（下から黒グラデ）+ タイトル + Rating + 小ドット（Missing/Conflict）
- リッチモード: サムネイル + タイトル + Actress（常時表示）

### Empty State

- 現状: `HardDriveUpload アイコン + "ライブラリが空です"`
- 改善: 棚モチーフ illustration + 「コレクションを始めよう」+ Import ボタン

### 採用しない UX 提案

- サイドパネル Detail スライドアウト — 工数対効果が合わない
- Excel スプレッドシートモード — Constitution で明示禁止

---

## 6. Metadata Improvements

### 採用

1. Tier1ExitFields の動的化（C2） — 上記 Refactoring Plan 参照
2. SupportedMediaTypes 厳格化（M2）— `p.CanHandle(work.MediaType)` で事前フィルタ
3. Evidence 重複スコア抑制（L3）— 低優先

### 却下

- EAV に `DataType` 列 — 文字列パースで機能しており過剰設計
- Language Tags per Row — Phase 3 以降
- External IDs (tmdb_id 等) — WISE スコープ外

---

## 7. Scraping Improvements

### Phase 1: 基盤強化（Sprint 23）

```csharp
// Polly: 全 Provider の HttpClient に適用
services.AddHttpClient<TProvider>()
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));
```

### Phase 2: レートリミット（Sprint 26）

- `RateLimiterService`: ドメイン別 Token Bucket、appsettings.json 設定
- レスポンスキャッシュ: SQLite、TTL=24h

### Phase 3: Cloudflare 対応（Sprint 27+）

- JavBus: Playwright + stealth plugin で動的回避

### Provider Priority（確定版）

**Video:** FANZA(80) > DLSite(80) > MGS(70) > FC2(60) > JavBus(50) > AvWiki(40)  
**Comic/Book:** DLSite(80) > Getchu(70) > ComicInfoXml(45) > DoujinishiFilename(30)

---

## 8. Sprint Backlog

### Sprint 23 — 安定性・信頼性

**Goal:** インポートが壊れない、メタデータ取得が transient error で死なない  
**Deliverables:**
- `ImportUseCase.cs`: EnumerateSafe（UnauthorizedAccess スキップ + streaming）
- Polly NuGet、全 HttpClient に exponential retry（3回、2^n 秒）
- `MetadataService.cs`: GetTier1ExitFields(MediaType) 動的化
- `MetadataService.cs`: SupportedMediaTypes 事前フィルタ

**Acceptance Criteria:**
- 制限ディレクトリ混在フォルダの Analyze が部分結果を返す（500 にならない）
- Comic Work のメタデータ取得で FANZA が発火しない
- ネットワーク cut 時にメタデータ取得が retry してから fail する（即死しない）

**Difficulty:** Medium

---

### Sprint 24 — DB & Gallery Performance

**Goal:** 10,000 作品以上でも Gallery が快適  
**Deliverables:**
- Migration: MetadataFields `(FieldName, Value)` + Works `Status/MediaType/Favorite` インデックス
- Cover API に ETag + Cache-Control 追加
- FTS5 更新をバックグラウンドジョブに分離

**Acceptance Criteria:**
- MetadataFields の Title 検索が INDEX SEARCH（SCAN でない）
- Cover 画像 2回目リクエストで 304 が返る

**Difficulty:** Low〜Medium

---

### Sprint 25 — Detail Page UX

**Goal:** Play/Read ボタンが即座に見える、技術情報はデフォルト隠蔽  
**Deliverables:**
- Detail Page Zone 1〜4 再構成
- Play/Read ボタンをヒーローエリアに昇格
- Assets/History を accordion（デフォルト閉じ）
- Diagnostics を DeveloperMode フラグ制御
- WorkCard Normal/Compact: hover 前はカバーのみ
- Empty State の onboarding 改善

**Acceptance Criteria:**
- Detail ページ到達後、スクロールなしで Play/Read ボタンが見える
- AppSettings DeveloperMode=false のとき Diagnostics セクション非表示

**Difficulty:** Medium

---

### Sprint 26 — Scraping 強化

**Goal:** レートリミット + レスポンスキャッシュで provider がより安定  
**Deliverables:**
- RateLimiterService（ドメイン別 Token Bucket）
- HTTP レスポンスキャッシュ（SQLite、TTL=24h）

**Acceptance Criteria:**
- FANZA への連続リクエストが設定値（例: 2req/s）を超えない
- 同一 Work の再メタデータ取得がキャッシュを返す

**Difficulty:** High

---

## 9. What NOT to Build

| 却下内容 | 理由 |
|----------|------|
| Excel スプレッドシート / データグリッドモード | Constitution で「管理ツールではない」と明示禁止 |
| サイドパネル Detail スライドアウト | 工数対効果が合わない。Detail ページ改善で代替 |
| 外部 DB ID（tmdb_id, vndb_id）専用フィールド | WISE スコープ外コンテンツ向け |
| AI 顔認識 / OCR / シーン検出 | Phase 5。今実装すると全体が止まる |
| Maker 公式サイト個別プロバイダー（SOD/S1等） | HTML変更で毎回壊れる。維持コスト > 価値 |
| ヒエラルキカルタグ | フラットタグで十分。Phase 3 で再評価 |
| フランチャイズ / 作品リレーション | Phase 3 Collection で対応 |
| Bulk Edit / 複数選択タグ付け | Phase 2〜3。先にコレクション機能を仕上げる |
| ストレージドライブ認識 | OS/エクスプローラーの責務 |

---

*End of CONSOLIDATED_REVIEW.md — 2026-07-01*
