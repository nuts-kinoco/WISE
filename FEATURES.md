# WISE — 実装済み機能一覧

> 最終更新: 2026-06-30  
> スタック: ASP.NET Core 8 Web API + Next.js 15 (TypeScript) + SQLite (EF Core 8)

---

## 目次

1. [アーキテクチャ概要](#1-アーキテクチャ概要)
2. [インポートパイプライン](#2-インポートパイプライン)
3. [メタデータパイプライン](#3-メタデータパイプライン)
4. [メタデータプロバイダー](#4-メタデータプロバイダー)
5. [ファイル整理](#5-ファイル整理)
6. [ギャラリー（一覧表示）](#6-ギャラリー一覧表示)
7. [作品詳細ページ](#7-作品詳細ページ)
8. [一括整理ページ（Organize）](#8-一括整理ページorganize)
9. [重複検出・解決](#9-重複検出解決)
10. [ジョブ管理](#10-ジョブ管理)
11. [設定・Cookie管理](#11-設定cookie管理)
12. [イベントログ・履歴](#12-イベントログ履歴)
13. [ウォッチフォルダ](#13-ウォッチフォルダ)
14. [REST API エンドポイント一覧](#14-rest-api-エンドポイント一覧)

---

## 1. アーキテクチャ概要

```
WISE.Domain          ドメインモデル・インターフェース（Entity, Enum, IMetadataProvider）
WISE.Application     ユースケース・サービス（MetadataService, ConflictResolver）
WISE.Infrastructure  DBコンテキスト・プロバイダー実装・Cookie管理
WISE.Api             ASP.NET Core Web API（Controllers, UseCases, BackgroundWorker）
wise-web             Next.js 15 フロントエンド
```

- **DB**: SQLite (`wise.db`)。EF Core 8 で管理。
- **バックグラウンド処理**: `BackgroundJobWorker`（`IHostedService`）が 2 秒ポーリングでジョブキューを消費。
- **ジョブ種別**: `Import`（ファイル取込）/ `FetchMetadata`（メタデータ取得）
- **キャンセル**: `IJobCancellationService` 経由で実行中ジョブをキャンセル可能。

---

## 2. インポートパイプライン

### トリガー
フロントエンドの `/import` ページ、またはウォッチフォルダ自動トリガー。

### 処理フロー（`ExecuteImportJobUseCase`）

1. **ファイル収集** — 入力フォルダ・ファイルリストを走査し `.mp4/.mkv/.avi/.zip/.jpg/.png` を列挙。重複パスを除去。
2. **識別子解決**（`IIdentifierResolver`） — ファイル名から品番（識別子）を抽出。エビデンスベースのスコアリングで確信度を算出し、`Diagnostic` ペイロードとして EventLog に保存。
3. **Move / Copy** — `ImportMode` に応じて出力フォルダへ移動またはコピー（`IOutputPathResolver` でパス生成）。
4. **Work 作成・マージ** — 同一識別子の Work が既に存在すれば Asset を追加（重複マージ）、なければ新規 Work を作成。
5. **メタデータジョブ自動登録** — `UseMetadataPipeline=true` の場合、新規 Work 全件の `FetchMetadata` ジョブをキューに追加。

### フロントエンド (`/import`)
- ディレクトリ選択ダイアログ（Windows FolderBrowserDialog 経由 `GET /api/system/browse-folder`）
- ImportMode 選択（Move / Copy / None）
- 進捗表示: `処理済み / 合計 件完了 (xx%)` + ETA（残り約N分M秒、比率>5%で表示）
- アクティブなスキャンジョブをチップで表示

---

## 3. メタデータパイプライン

### 処理フロー（`FetchMetadataJobUseCase`）

| ステップ | 内容 |
|---|---|
| 1. 孤立アセット削除 | DBに記録されているがディスクに存在しないアセットを削除 |
| 2. プロバイダー実行 | `MetadataService.CollectResultsAsync()`（後述の Tier 分割）|
| 3. カバー画像ダウンロード | PortraitCover / LandscapeCover を信頼度順に試行、20KB未満は却下 |
| 4. サンプル画像ダウンロード | 設定 `downloadSampleImages=true` のとき最大10枚を `.thumbnails/` に保存 |
| 5. FFmpeg サムネイル | PortraitCover が取得できなかった場合に動画フレームからサムネイルを生成 |
| 6. 複数女優処理 | 2名以上検出時は全員を `ActressTag` に昇格し `Actress` をクリア |
| 7. メタデータ適用 | `Work.ApplyResolvedMetadata()` でDBフィールドを更新 |
| 8. ファイル整理 | 女優名フォルダ構造へ再配置（`LibraryRoot/女優名/識別子/`）|
| 9. ファイル出力 | `metadata.json` を作業ディレクトリに書き出し |
| 10. 完了判定 | Title + PortraitCover + Identifier が揃えば `Organizing`、それ以外は `Failed` |

### Tier 戦略（`MetadataService`）

```
Tier1 (Priority≥80): FANZA — 並列実行後、Title/Actress/Maker が全て揃えば Tier2 をスキップ
Tier2 (Priority<80): MGS / FC2 / FC2Alt / AvWiki / JavBus / LocalNfo — Tier1 不足時に並列実行
```

### 競合解決（`MetadataConflictResolver`）

同一フィールドに複数の候補がある場合、`Confidence → Priority → FetchedAt` の順で降順ソートし、最上位を Primary に昇格。

### `metadata.json` フォーマット（作業ディレクトリに保存）

```json
{
  "identifier": "ABW-123",
  "provider": "Fanza",
  "providers": ["Fanza", "AvWiki"],
  "url": "https://...",
  "scrapedAt": "2026-06-30T00:00:00Z",
  "title": "...",
  "maker": "...", "label": "...", "series": "...",
  "releaseDate": "2026-01-15",
  "runtime": 120,
  "actresses": [{ "name": "..." }],
  "genres": ["..."],
  "covers": { "portrait": "abw-123_ps.jpg", "landscape": "abw-123_pl.jpg" },
  "sampleImages": ["/api/assets/{id}/content"],
  "userFavorite": false,
  "userRating": null,
  "userMemo": ""
}
```

---

## 4. メタデータプロバイダー

| プロバイダー | ProviderId | Priority | 対象 | 取得フィールド |
|---|---|---|---|---|
| FANZA | `Fanza` | 80 | 一般AV | Title/Actress/Maker/Label/Series/ReleaseDate/Genre/Runtime/PortraitCover/LandscapeCover/SampleImage |
| MGStage | `Mgs` | 70 | 一般AV | Title/Actress/Maker/Label/ReleaseDate/Genre/Runtime/PortraitCover/LandscapeCover |
| FC2 Content Market | `Fc2` | 60 | FC2-PPV | Title/Maker/PortraitCover（Cookieなしでは年齢ゲートで失敗する場合あり）|
| av-wiki | `AvWiki` | 60 | 一般AV | Title/Actress/Maker/PortraitCover |
| FC2 Alt | `Fc2Alt` | 55 | FC2-PPV（削除済み） | MissAV→bestjavporn→javdock→123AV のフォールバックチェーン |
| JavBus | `JavBus` | 50 | 一般AV | Title/Actress/Maker/Label/Genre/ReleaseDate/PortraitCover |
| LocalNfo | `LocalNfo` | 40 | ローカル `.nfo` | 全フィールド（ローカルキャッシュ再利用） |

### FANZA の詳細

- **Playwright** で `video.dmm.co.jp` を JS レンダリングしてスクレイピング（プロセス起動時に Chromium を自動インストール）。
- **HtmlAgilityPack** でのフォールバックあり（`www.dmm.co.jp` SPA shell から Cover のみ）。
- **Cookie 注入**: `ICookieProvider` 経由（`%APPDATA%\WISE\fanzaStorageState.json` または `fanzaCookies.txt`）。

### FC2 の詳細

- `https://adult.contents.fc2.com/article/{numericId}/` をスクレイピング。
- **年齢認証ゲート判定**: `og:title` が空 かつ 特定フレーズ（"年齢認証","18歳未満","adult check","age verification"）を含む場合のみゲートと判定（誤検知防止）。
- **Cookie 注入**: `ICookieProvider` 経由（`%APPDATA%\WISE\fc2StorageState.json` 優先、なければ `fc2Cookies.txt`）。
- **Maker は任意**（FC2では出品者削除によりリンク自体が存在しない場合がある）。

### FC2 Alt の詳細

FC2 公式ページが存在しない・削除済みの場合のフォールバック。以下を並列試行:
1. **MissAV** — Title/PortraitCover
2. **bestjavporn** — Title/PortraitCover
3. **javdock** — Title/PortraitCover
4. **123AV** — Title/Maker/PortraitCover（"FC2" カテゴリは除外）

---

## 5. ファイル整理

### ディレクトリ構造

```
LibraryRoot/
  女優名/
    識別子/
      識別子.mp4
      識別子_ps.jpg          ← PortraitCover
      識別子_pl.jpg          ← LandscapeCover
      metadata.json
      .thumbnails/
        sample_00.jpg        ← サンプル画像
        thumbnail.jpg        ← FFmpegサムネイル（フォールバック）
        upload_*.jpg         ← ユーザーアップロード画像
```

- **複数女優**: `複数女優/識別子/` フォルダに配置。
- **リトライ安全**: 既に整理済みのフォルダ構造を検出して無限ループを回避。
- **空ディレクトリ自動削除**: 移動後に元ディレクトリが空になれば削除。

### カバー画像手動変更

- サムネイル・サンプル画像のピッカー UI（`/api/works/{id}/thumbnail-assets`）
- ドラッグ＆ドロップでのカバー画像アップロード（`POST /api/works/{id}/upload-cover`）
- 元の provider カバーへの復元機能

---

## 6. ギャラリー（一覧表示）

**フロントエンド**: `/` (トップページ)

- ポートレートカバーのグリッド表示（TanStack Query でページネーション取得）
- **検索**: 品番・タイトル・メーカー・出演者・レーベル・ジャンルの全文検索（`?q=`）
- **ステータスフィルター**: `Organizing / Failed / ScanPending` 等
- **ソート**: 品番 / タイトル / 出演者 / メーカー / ステータス / 評価 / お気に入り
- **ページネーション**: 50件/ページ
- **WorkCard**: カバー表示、お気に入りトグル、評価表示、ステータスチップ
- フォルダを開く（`POST /api/works/{id}/open-folder`→エクスプローラーで選択状態で開く）

---

## 7. 作品詳細ページ

**フロントエンド**: `/works/{id}`

### 表示情報
- PortraitCover / LandscapeCover
- 品番・タイトル・出演者・メーカー・レーベル・シリーズ・発売日・収録時間・ジャンル
- サンプル画像ギャラリー（横スクロール）

### ユーザーデータ編集
- **お気に入り**: ハートアイコンをトグル（`PATCH /api/works/{id}/user-data`）
- **評価**: 星1〜5（同じ星をクリックでクリア）
- **メモ**: テキストエリア（DB + `metadata.json` に保存）

### タグ管理
- **ユーザータグ** (`UserTag`): 追加・削除（削除前に確認ダイアログを表示）
- **ジャンルタグ** (`Genre`): スクレイピングで取得済みタグを個別削除

### メタデータ
- 全プロバイダーのフィールド一覧（FieldName / Value / Provider / Confidence / Primary）
- プロバイダー診断（成功/失敗、レイテンシ）

### アクション
- **メタデータ再取得**: `POST /api/jobs/fetchmetadata`（即時ジョブキュー）
- **フォルダを開く**: エクスプローラーで動画ファイルを選択状態で表示
- **作品削除**: DBのみ削除 or ファイルも物理削除（確認ダイアログあり）
- **カバー変更**: ピッカーで `.thumbnails/` 内の画像を選択、または画像アップロード

### イベント履歴
作品に紐づくイベント（インポート・メタデータ取得・整理）をタイムラインで表示。

### 診断情報
インポート時のIdentifier解決結果（Evidences・Confidence・Decision）を折りたたみで表示。

---

## 8. 一括整理ページ（Organize）

**フロントエンド**: `/organize`

全作品をテーブルビューで一覧表示し、評価・お気に入りをインラインで編集できる。

### 機能

| 機能 | 詳細 |
|---|---|
| 全件読み込み | 50件/ページ単位で並列フェッチし、クライアント側で集約 |
| ソート | 品番・タイトル・出演者・メーカー・ステータス・評価・お気に入りをヘッダークリックで昇降順切替 |
| 絞り込み | ステータスドロップダウン + 品番/タイトルのデバウンスサーチ（300ms） |
| ページネーション | 50件/ページ |
| **インライン評価** | 各行で星クリック。同じ星を再度クリックするとクリア（null）|
| **インラインお気に入り** | 各行でハートアイコンをトグル |
| 変更ハイライト | 未確定の変更行を amber 色でハイライト、品番セルに黄色ドットを表示 |
| 変更カウント | ヘッダーに「N件 変更中」バッジ |
| **Undo** | 最後の1操作を取り消す（操作スタック管理）|
| **全て戻す** | 全変更を一括破棄 |
| **確定する** | `PATCH /api/works/{id}/user-data` を全変更分並列送信後、ローカル状態を更新 |
| Failed 再スキャン | Failed ステータスの作品を一括で `FetchMetadata` ジョブキューに追加 |

---

## 9. 重複検出・解決

**フロントエンド**: `/duplicates`

### 検出ロジック（`GET /api/duplicates`）

| 検出タイプ | 方法 | バッジ |
|---|---|---|
| `identifier` | `PrimaryIdentifier` の大文字統一完全一致 | 「品番一致」（青） |
| `title` | タイトルを正規化（小文字化・記号除去・空白正規化）して 8文字以上で一致 | 「タイトル類似」（紫） |

品番重複で検出済みの Work はタイトル類似検索の対象外。

### 解決フロー（`POST /api/duplicates/resolve`）

1. **Keep 選択**: デフォルトは最大ファイルサイズの Work。
2. **マージオプション**: 評価・メモ・UserTag を Keep Work に引き継ぐ（Keep 側が空の場合のみ）。
3. **一括削除**: `DeleteWorkIds[]` 対応（3件以上の重複グループに対応）。
4. **ファイル削除**: オプションで物理ファイルも削除（空フォルダは自動削除）。
5. **DB クリーンアップ**: 対象 Work に紐づく Jobs・EventLogs も削除。

---

## 10. ジョブ管理

**フロントエンド**: `/jobs`

### ジョブ種別

| 種別 | 内容 |
|---|---|
| `Import` | ファイルの取込・識別子解決・Work 作成 |
| `FetchMetadata` | 指定 Work のメタデータ取得・ファイル整理 |

### API

| エンドポイント | 内容 |
|---|---|
| `GET /api/jobs` | 最新100件のジョブ一覧 |
| `GET /api/jobs/active` | 実行中・待機中のジョブ（Work 品番付き） |
| `GET /api/jobs/{id}` | 個別ジョブ詳細 |
| `POST /api/jobs/import` | Import ジョブをキューに追加 |
| `POST /api/jobs/fetchmetadata` | 単一 Work の FetchMetadata ジョブ追加 |
| `POST /api/jobs/fetchmetadata/batch` | 複数 Work の FetchMetadata ジョブ一括追加 |
| `POST /api/jobs/{id}/cancel` | 実行中ジョブをキャンセル |
| `POST /api/jobs/{id}/retry` | 失敗・キャンセルジョブを再キュー |
| `DELETE /api/jobs` | 完了・失敗・キャンセル済みジョブを一括削除 |
| `GET /api/jobs/providers` | 登録済みプロバイダー一覧（ID + Priority）|

---

## 11. 設定・Cookie管理

**フロントエンド**: `/settings`

### アプリケーション設定

| キー | デフォルト | 内容 |
|---|---|---|
| `downloadSampleImages` | `false` | サンプル画像を最大10枚ダウンロードするか |

### Cookie 管理

**FANZA**:
- `%APPDATA%\WISE\fanzaStorageState.json` — Playwright Storage State 形式（優先）
- `%APPDATA%\WISE\fanzaCookies.txt` — Cookie ヘッダー文字列形式

**FC2**:
- `%APPDATA%\WISE\fc2StorageState.json` — Playwright Storage State 形式（優先）
- `%APPDATA%\WISE\fc2Cookies.txt` — Cookie ヘッダー文字列形式
- **フロントエンドから設定可能**: 設定ページに Cookie 貼り付け UI → `POST /api/system/cookies/fc2`
- **ステータス確認**: `GET /api/system/cookies/fc2/status`（ファイル存在確認 + プレビュー）

### Cookie の適用タイミング

`ICookieProvider` / `ICookiePolicy` が各プロバイダーに DI 注入され、HTTP リクエスト時に `Cookie:` ヘッダーとして付与される。

---

## 12. イベントログ・履歴

**フロントエンド**: `/history`

### 記録されるイベント

| イベントタイプ | 発生タイミング |
|---|---|
| `Work Created` | インポート時に新規 Work を作成（Diagnostic ペイロード付き）|
| `Asset Added` | ファイルが Work に紐づけられたとき |
| `Duplicate Merged` | 既存 Work にアセットを追加マージしたとき |
| `Import Completed` | Import ジョブ完了時 |
| `Portrait Cover Downloaded` | PortraitCover のダウンロード成功時 |
| `Landscape Cover Downloaded` | LandscapeCover のダウンロード成功時 |
| `Thumbnail Generated` | FFmpeg によるサムネイル生成時 |
| `Files Organized` | 女優名フォルダへの再配置完了時 |
| `Metadata Fetched` | メタデータ取得完了時 |

### API

| エンドポイント | 内容 |
|---|---|
| `GET /api/system/history?limit=200` | 最新N件のイベント一覧 |
| `GET /api/system/history/count` | イベント件数 |
| `DELETE /api/system/history` | 全イベントログを削除 |

---

## 13. ウォッチフォルダ

**フロントエンド**: `/settings`（設定ページ内）

指定フォルダを自動監視し、新着ファイルを Import するための仕組み。

### API

| エンドポイント | 内容 |
|---|---|
| `GET /api/watchfolders` | 登録済みフォルダ一覧 |
| `POST /api/watchfolders` | フォルダを追加 |
| `DELETE /api/watchfolders/{id}` | フォルダを削除 |
| `PATCH /api/watchfolders/{id}/toggle` | 有効/無効を切り替え |

---

## 14. REST API エンドポイント一覧

### Works

| メソッド | パス | 内容 |
|---|---|---|
| `GET` | `/api/works` | 作品一覧（?page, ?pageSize, ?q, ?status）|
| `GET` | `/api/works/{id}` | 作品詳細（メタデータ・アセット・履歴・診断）|
| `PATCH` | `/api/works/{id}/user-data` | お気に入り・評価・メモ更新 |
| `PATCH` | `/api/works/{id}/metadata` | 手動メタデータ上書き（Triage用、priority=999）|
| `GET` | `/api/works/{id}/thumbnail-assets` | `.thumbnails/` 内の画像アセット一覧 |
| `POST` | `/api/works/{id}/set-cover` | カバー画像を指定アセットに変更 |
| `POST` | `/api/works/{id}/upload-cover` | 画像ファイルをアップロードしてカバーに設定 |
| `POST` | `/api/works/{id}/open-folder` | エクスプローラーで動画ファイルを表示 |
| `POST` | `/api/works/{id}/user-tags` | ユーザータグ追加 |
| `DELETE` | `/api/works/{id}/user-tags/{value}` | ユーザータグ削除 |
| `DELETE` | `/api/works/{id}/genre-tags/{value}` | ジャンルタグ個別削除 |
| `DELETE` | `/api/works/{id}?deleteFiles=` | 作品削除（ファイル削除オプション付き）|

### Assets

| メソッド | パス | 内容 |
|---|---|---|
| `GET` | `/api/assets/{id}/content` | アセットファイルをバイナリ配信 |

### Import

| メソッド | パス | 内容 |
|---|---|---|
| `POST` | `/api/import/analyze` | ディレクトリを解析（Work 作成は行わない）|

### Jobs

（[ジョブ管理 API 参照](#10-ジョブ管理)）

### Duplicates

| メソッド | パス | 内容 |
|---|---|---|
| `GET` | `/api/duplicates` | 重複グループ一覧（品番一致 + タイトル類似）|
| `POST` | `/api/duplicates/resolve` | 重複を解決（複数削除対応）|

### System

| メソッド | パス | 内容 |
|---|---|---|
| `GET` | `/api/system/history` | イベントログ |
| `DELETE` | `/api/system/history` | イベントログ全削除 |
| `POST` | `/api/system/cookies/fc2` | FC2 Cookie を保存 |
| `GET` | `/api/system/cookies/fc2/status` | FC2 Cookie の状態確認 |
| `POST` | `/api/system/open-path` | 指定パスをエクスプローラーで開く |
| `GET` | `/api/system/browse-folder` | フォルダ選択ダイアログを開く（Windows）|

### Settings / WatchFolders

（[設定 API 参照](#11-設定cookie管理)、[ウォッチフォルダ API 参照](#13-ウォッチフォルダ)）

---

## フロントエンド ページ一覧

| URL | 説明 |
|---|---|
| `/` | ギャラリー（カードグリッド）|
| `/works/{id}` | 作品詳細 |
| `/import` | ファイルインポート |
| `/organize` | 一括整理（インライン編集テーブル）|
| `/duplicates` | 重複検出・解決 |
| `/jobs` | ジョブ一覧・管理 |
| `/history` | イベントログ |
| `/settings` | アプリ設定・Cookie 管理・ウォッチフォルダ |
| `/triage` | トリアージ（手動メタデータ入力）|
