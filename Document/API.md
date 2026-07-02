# WISE v2 API.md (v1.0)

> **本書は新規作成（v1.0）である。**  
> WISE v2 の REST API 全エンドポイントを定義する。既存エンドポイント（Works/Assets/Jobs/System）と、v2新規エンドポイント（ReadingHistory/Viewer/DisplayProfile）を含む。

前提資料：**Architecture.md v2.0**、**Domain.md v2.0**

---

# 1. API設計思想

## 1.1 基本方針

- **RESTful** — Resource中心の設計。URLはリソースを表し、HTTPメソッドで操作を表現する
- **localhost限定** — WISE v2はローカルアプリケーション。APIは `http://localhost:PORT` でのみ公開する
- **JSON** — リクエスト・レスポンスボディはすべてJSON（`Content-Type: application/json`）
- **MediaType非依存** — APIはMediaTypeの概念を意識するが、エンドポイントパスをMediaTypeで分岐させない
- **バイナリは別エンドポイント** — 画像・動画・アーカイブページのバイナリは独立したエンドポイントで提供

## 1.2 ベースURL

```
http://localhost:5255/api/
```

## 1.3 共通レスポンス形式

```json
// 成功（コレクション）
{
  "items": [...],
  "total": 1234,
  "page": 1,
  "pageSize": 50
}

// 成功（単一）
{
  "id": "uuid",
  ...
}

// エラー
{
  "error": {
    "code": "WORK_NOT_FOUND",
    "message": "Work with id '...' was not found."
  }
}
```

## 1.4 共通クエリパラメータ（コレクション系）

| パラメータ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `page` | int | 1 | ページ番号（1始まり） |
| `pageSize` | int | 50 | 1ページあたりの件数（最大: 500） |
| `sort` | string | `created_at` | ソートカラム |
| `order` | string | `desc` | `asc` / `desc` |

---

# 2. Works API

## 2.1 Work一覧取得

```
GET /api/works
```

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `q` | string | 全文検索クエリ（FTS5） |
| `mediaType` | int | MediaTypeフィルタ（1=Video, 2=Comic, 3=Book, ...） |
| `status` | string | `active`（デフォルト）/ `missing` / `all` |
| `favorite` | bool | お気に入りのみ |
| `collectionId` | uuid | 特定CollectionのWork一覧 |
| `isComplete` | bool | isComplete=trueのWorkのみ |

**レスポンス：**

```json
{
  "items": [
    {
      "id": "uuid",
      "primaryIdentifier": "FANZA-ABP-123",
      "confidenceScore": 85,
      "status": "active",
      "mediaType": 1,
      "coverUrl": "/api/works/{id}/cover",
      "metadata": {
        "title": "作品タイトル",
        "actress": ["女優A", "女優B"],
        "maker": "メーカー名",
        "releaseDate": "2024-01-15"
      },
      "isComplete": true,
      "lastReadAt": "2026-06-28T10:00:00Z",
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ],
  "total": 1234,
  "page": 1,
  "pageSize": 50
}
```

> **`metadata`フィールド：** `is_primary=true` のMetadataFieldのみを含む。表示フィールドはサーバー側では絞り込まず、フロントエンドのMediaDisplayProfileに従う。

## 2.2 Work詳細取得

```
GET /api/works/{id}
```

**レスポンス：**

```json
{
  "id": "uuid",
  "primaryIdentifier": "FANZA-ABP-123",
  "confidenceScore": 85,
  "status": "active",
  "mediaType": 1,
  "coverUrl": "/api/works/{id}/cover",
  "metadata": {
    "title": "...",
    "actress": [...],
    "maker": "...",
    "label": "...",
    "releaseDate": "...",
    "genre": [...],
    "description": "...",
    "sampleImageUrls": [...]
  },
  "assets": [
    {
      "id": "uuid",
      "role": "video",
      "storageFormat": "single_file",
      "filePath": "C:/Videos/ABP-123.mp4",
      "durationSec": 7200,
      "resolution": "1920x1080",
      "fileSize": 4294967296,
      "status": "active"
    }
  ],
  "evidences": [...],
  "isComplete": true,
  "createdAt": "...",
  "updatedAt": "..."
}
```

## 2.3 Work作成（手動）

```
POST /api/works
Content-Type: application/json

{
  "primaryIdentifier": "MANUAL-001",
  "mediaType": 2
}
```

## 2.4 Work更新

```
PATCH /api/works/{id}
Content-Type: application/json

{
  "metadata": {
    "title": "手動編集タイトル"
  }
}
```

> MetadataFieldの更新は `Manual` Providerとして記録され、最優先（Priority=100）で適用される。

## 2.5 Work削除

```
DELETE /api/works/{id}?deleteFiles={bool}
```

> ⚠️ **実装との乖離（P4監査で指摘）**: 本節はSoft Deleteと記載していたが、実装
> （`WorkFileUseCase.DeleteWorkAsync`）はWork行・関連MetadataField/Asset/Job/EventLogを
> **DBから物理削除**する。`deleteFiles=true`の場合はディスク上の物理ファイルも削除する
> （ファイルがロック中で削除できない場合は`409 Conflict`を返し、DBは変更しない）。
> Soft Delete化するかどうかは未決定の設計判断として残っている
> （`Document/Architecture.md` §1.2 の注記も参照）。

## 2.6 カバー画像取得

```
GET /api/works/{id}/cover
```

**レスポンス：** 画像バイナリ（`Content-Type: image/jpeg` 等）

- COVER_CACHEにキャッシュが存在する場合はキャッシュから返す
- キャッシュが存在しない場合はCoverExtractJobを高優先度で投入し、`202 Accepted` を返す（プレースホルダーとともに）

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `orientation` | string | `portrait`（デフォルト）/ `landscape` |

## 2.7 Work Metadata再取得

> ⚠️ **実装との乖離**: `POST /api/works/{id}/refresh-metadata` は存在しない。
> 実際のエンドポイントは以下（`JobsController`、レスポンス形状も異なる）:

```
POST /api/jobs/fetchmetadata
Body: { "workId": "uuid" }
→ { "jobId": "uuid", "workId": "uuid", "message": "FetchMetadata job queued." }

POST /api/jobs/fetchmetadata/batch
Body: { "workIds": ["uuid", ...] }
→ { "queued": 3 }
```

## 2.8 Evidence一覧（Diagnostic）

> ⚠️ **未実装**: `GET /api/works/{id}/evidences` は存在しない。Evidence永続化自体が
> 未実装のため（`Document/Architecture.md` §2.2 参照）。近い情報として
> `GET /api/works/{id}` レスポンスの `Diagnostic` フィールド（Work作成イベントの
> Payloadをパースしたもの、形状は不定）がある。

## 2.9 Viewer情報取得

```
GET /api/works/{id}/viewer-info
```

**レスポンス：**

```json
{
  "viewerRoute": "/viewer/comic",
  "capabilities": {
    "supportsResume": true,
    "supportsPageNavigation": true,
    "supportsZoom": true,
    "supportsPlaybackSpeed": false,
    "supportsBookmark": true,
    "supportsDoublePage": true
  },
  "resumePosition": {
    "pageNumber": 42,
    "positionSeconds": null,
    "positionPercent": 0.45,
    "lastReadAt": "2026-06-28T10:00:00Z"
  }
}
```

---

# 3. Assets API

> ⚠️ **実装との乖離**: `GET /api/works/{workId}/assets`（Asset一覧単独取得）は存在しない。
> Asset一覧は `GET /api/works/{id}` レスポンスの `Assets` フィールドに含まれる。
> また配信エンドポイントは `stream`/`thumbnail` に分かれておらず、動画・画像・サムネイル
> すべて単一のエンドポイントに統合されている。

## 3.1 Assetファイル配信（統合エンドポイント）

```
GET /api/assets/{id}/content
```

**レスポンス：** ファイルバイナリ（動画は`VideoStreamCache`によるRange Requests対応。
先頭最大32MBをメモリキャッシュし、キャッシュヒット時は`206 Partial Content`をメモリから、
ミス時は`PhysicalFile`でOSゼロコピー送信）。動画・カバー画像・サムネイル・サンプル画像等、
全AssetTypeがこのエンドポイントで統一的に配信される。

---

# 4. Reader API（v2新規 — Comic/Book専用）

## 4.1 アーカイブページリスト取得

```
GET /api/works/{id}/reader/pages
```

**レスポンス：**

```json
{
  "storageFormat": "archive",
  "totalPages": 48,
  "pages": [
    {
      "index": 0,
      "name": "001.jpg",
      "sizeBytes": 204800
    },
    {
      "index": 1,
      "name": "002.jpg",
      "sizeBytes": 198400
    }
  ]
}
```

- ArchiveIndexのキャッシュが存在する場合はキャッシュから返す
- キャッシュが存在しない場合はArchiveIndexJobを高優先度で投入し、`202 Accepted`

## 4.2 ページ画像取得

```
GET /api/works/{id}/reader/pages/{pageIndex}
```

**レスポンス：** 画像バイナリ（`Content-Type: image/jpeg` 等）

- IArchiveReaderがストリーミング（解凍なし）でページ画像を返す
- StorageFormatに応じてZipArchiveReader / FolderArchiveReader / PdfArchiveReaderを選択

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `width` | int | リサイズ幅（デフォルト: なし） |
| `quality` | int | JPEG品質（デフォルト: 85） |

---

# 5. ReadingHistory API（v2新規）

## 5.1 進捗取得

```
GET /api/works/{id}/reading-history
```

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `deviceId` | string | デバイスID（省略時はリクエストデバイス） |

**レスポンス：**

```json
{
  "workId": "uuid",
  "deviceId": "device-uuid",
  "pageNumber": 42,
  "positionSeconds": null,
  "positionPercent": 0.45,
  "lastReadAt": "2026-06-28T10:00:00Z"
}
```

## 5.2 進捗更新

```
PUT /api/works/{id}/reading-history
Content-Type: application/json

{
  "deviceId": "device-uuid",
  "pageNumber": 43,
  "positionSeconds": null,
  "positionPercent": 0.47
}
```

**レスポンス：** `204 No Content`（UPSERT）

## 5.3 進捗リセット

```
DELETE /api/works/{id}/reading-history
Content-Type: application/json

{
  "deviceId": "device-uuid"
}
```

---

# 6. Collections API

## 6.1 Collection一覧

```
GET /api/collections
```

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `type` | string | `Favorite`, `Author`, `Circle`, `Series` 等 |

## 6.2 Collection作成

```
POST /api/collections
Content-Type: application/json

{
  "name": "お気に入り作家 - 田中花子",
  "type": "Author",
  "ruleDefinition": null
}
```

## 6.3 CollectionへのWork追加

```
POST /api/collections/{id}/works
Content-Type: application/json

{
  "workId": "uuid",
  "sortOrder": 10
}
```

## 6.4 Smart Folder

```
POST /api/collections
Content-Type: application/json

{
  "name": "2024年発売の百合マンガ",
  "type": "SmartFolder",
  "ruleDefinition": "{\"and\":[{\"field\":\"genre\",\"contains\":\"百合\"},{\"field\":\"release_date\",\"gte\":\"2024-01-01\"},{\"field\":\"media_type\",\"eq\":2}]}"
}
```

---

# 7. Jobs API

## 7.1 Job一覧

```
GET /api/jobs
```

**クエリパラメータ：**

| パラメータ | 型 | 説明 |
|---|---|---|
| `status` | string | `Queued` / `Running` / `Succeeded` / `Failed` |
| `jobType` | string | `MetadataFetch` / `CoverExtract` / `ArchiveIndex` 等 |
| `workId` | uuid | Work別Job一覧 |

**レスポンス：**

```json
{
  "items": [
    {
      "id": "uuid",
      "workId": "uuid",
      "jobType": "MetadataFetch",
      "status": "Running",
      "priority": 70,
      "retryCount": 0,
      "scheduledAt": "...",
      "startedAt": "...",
      "createdAt": "..."
    }
  ],
  "total": 5,
  "runningCount": 2,
  "queuedCount": 3
}
```

## 7.2 Jobキャンセル

```
DELETE /api/jobs/{id}
```

## 7.3 全Jobキャンセル（種別指定）

```
DELETE /api/jobs?jobType=MetadataFetch&status=Queued
```

---

# 8. Import API

## 8.1 インポートジョブ開始

```
POST /api/import
Content-Type: application/json

{
  "paths": ["C:/Comics", "D:/Videos"],
  "recursive": true,
  "mediaTypeHint": null
}
```

**レスポンス：**

```json
{
  "jobId": "uuid",
  "status": "queued",
  "estimatedFiles": 1234
}
```

## 8.2 インポート状況確認

```
GET /api/import/{jobId}/status
```

**レスポンス：**

```json
{
  "jobId": "uuid",
  "status": "Running",
  "processedFiles": 456,
  "totalFiles": 1234,
  "newWorks": 120,
  "updatedWorks": 30,
  "errors": 2
}
```

---

# 9. Settings API

## 9.1 DisplayProfile取得（MediaType別）

```
GET /api/settings/display-profiles/{mediaType}
```

**レスポンス：**

```json
{
  "mediaType": 2,
  "coverOrientation": "portrait",
  "defaultSort": "created_at DESC",
  "isUserCustomized": false,
  "galleryFields": [
    { "fieldName": "title", "label": "タイトル", "isVisible": true, "order": 1 },
    { "fieldName": "circle", "label": "サークル", "isVisible": true, "order": 2 },
    { "fieldName": "author", "label": "作者", "isVisible": true, "order": 3 },
    { "fieldName": "page_count", "label": "ページ数", "isVisible": true, "order": 4 },
    { "fieldName": "release_date", "label": "発売日", "isVisible": false, "order": 5 }
  ],
  "detailFields": [...]
}
```

## 9.2 DisplayProfile更新（ユーザーカスタマイズ）

```
PATCH /api/settings/display-profiles/{mediaType}
Content-Type: application/json

{
  "coverOrientation": "portrait",
  "galleryFields": [
    { "fieldName": "title", "isVisible": true, "order": 1 },
    { "fieldName": "circle", "isVisible": false, "order": 2 }
  ]
}
```

**レスポンス：** `200 OK`（更新後のProfile）

> `is_user_customized = true` に自動で更新される。

## 9.3 DisplayProfileリセット（デフォルト復元）

```
DELETE /api/settings/display-profiles/{mediaType}
```

**レスポンス：** `200 OK`（デフォルトProfile）

## 9.4 全DisplayProfile一覧

```
GET /api/settings/display-profiles
```

## 9.5 Provider一覧・設定

```
GET /api/settings/providers
```

**レスポンス：**

```json
{
  "items": [
    {
      "id": "uuid",
      "name": "FANZA",
      "type": "scraping",
      "isEnabled": true,
      "priority": 80,
      "supportedMediaTypes": [1],
      "lastSuccessAt": "2026-06-28T09:00:00Z",
      "successRate": 0.95
    }
  ]
}
```

```
PATCH /api/settings/providers/{id}
Content-Type: application/json

{
  "isEnabled": false,
  "priority": 70
}
```

---

# 10. System API

## 10.1 システム状態

```
GET /api/system/status
```

**レスポンス：**

```json
{
  "version": "2.0.0",
  "dbPath": "C:/Users/.../wise.db",
  "dbSizeBytes": 104857600,
  "workCount": 5432,
  "assetCount": 5678,
  "jobQueue": {
    "queued": 3,
    "running": 2,
    "failed": 1
  },
  "fts5LastUpdated": "2026-06-28T09:50:00Z"
}
```

## 10.2 DB最適化（VACUUM）

```
POST /api/system/optimize
```

## 10.3 FTS5インデックス再構築

```
POST /api/system/reindex-fts
```

---

# 11. History（Event Log）API

> ⚠️ **実装との乖離**: `GET /api/works/{id}/history`（Work別History単独取得）は存在しない。
> Work別の履歴は `GET /api/works/{id}` レスポンスの `History` フィールドに含まれる
> （`EventType`/`OccurredAt`/`Actor`/`Payload`）。全履歴の横断取得は以下：

```
GET /api/history          # 直近100件、TargetIdentifier解決済み（フロントの履歴画面が使用）
GET /api/system/history?limit={n}   # 同等機能の別実装（フロント未使用、後方互換で維持）
GET /api/system/history/count
DELETE /api/system/history          # 履歴一括削除（設定ページ「メンテナンス」で使用）
```

---

# 12. Duplicates API（未記載だった実装済みエンドポイント）

## 12.1 重複グループ検出

```
GET /api/duplicates
```

品番（PrimaryIdentifier）完全一致、またはタイトル正規化一致で重複グループを検出して返す。
`detectionType`: `"identifier"` または `"title"`。

## 12.2 重複解消

```
POST /api/duplicates/resolve
Body: {
  "keepWorkId": "uuid",
  "deleteWorkIds": ["uuid", ...],
  "deleteFiles": false,
  "mergeRating": true,
  "mergeMemo": true,
  "mergeUserTags": true,
  "mergeFavorite": true
}
→ { "resolved": true, "filesDeleted": 0, "filesFailed": 0 }
```

`keepWorkId`を残し、`deleteWorkIds`を全て削除する（3件以上対応）。マージ系オプションが
trueのフィールドについて、`keepWork`側が未設定の場合のみ`deleteWork`側の値を引き継ぐ
（Favoriteのみ「どちらかがtrueならtrue」の論理和）。DB書き込み（マージ＋行削除）は
単一トランザクションで全件成功/全件失敗を保証する。物理ファイル削除はDBコミット後の
ベストエフォート。

---

# 13. Home API（未記載だった実装済みエンドポイント）

```
GET /api/home?deviceId={string}
→ { "continueWatching": [...], "recentlyAdded": [...], "favorites": [...] }

GET /api/home/random
→ 単一のWorkアイテム（ランダム1件）
```

---

# 14. Works API 追加エンドポイント（§2で未記載だった実装済みエンドポイント）

```
PATCH  /api/works/{id}/user-data          # Favorite/Rating/Memo更新
PATCH  /api/works/{id}/metadata           # トリアージ用手動メタデータ上書き（ConfidenceScore=999）
GET    /api/works/{id}/related?field=&limit=  # 関連作品（Actress/Series/Circle/Author/Maker一致）
GET    /api/works/{id}/thumbnail-assets   # .thumbnails/配下のカバー候補一覧
POST   /api/works/{id}/set-cover          # 既存アセットをカバーとして選択
POST   /api/works/{id}/upload-cover       # 画像D&Dアップロード→カバー設定（multipart/form-data）
POST   /api/works/{id}/open-folder        # ファイルの場所をエクスプローラーで開く（Windows専用）
POST   /api/works/{id}/user-tags          # ユーザータグ追加
DELETE /api/works/{id}/user-tags/{value}
DELETE /api/works/{id}/genre-tags/{value} # ジャンルタグの個別削除（|区切り値から1件除去）
GET    /api/works/{id}/epub               # EPUB配信
```

---

# 15. エラーコード一覧

| HTTPステータス | エラーコード | 説明 |
|---|---|---|
| 404 | `WORK_NOT_FOUND` | Work ID が存在しない |
| 404 | `ASSET_NOT_FOUND` | Asset ID が存在しない |
| 404 | `PAGE_NOT_FOUND` | ページインデックスが範囲外 |
| 400 | `INVALID_MEDIA_TYPE` | MediaType値が不正 |
| 400 | `INVALID_SORT` | ソートカラムが不正 |
| 409 | `IDENTIFIER_CONFLICT` | primary_identifierが既に存在する |
| 422 | `ARCHIVE_NOT_READY` | ArchiveIndexがまだ構築されていない |
| 503 | `PROVIDER_UNAVAILABLE` | ProviderのCircuit Breakerが発動中 |

---

# 16. 認証

WISE v2は認証なし（localhost限定のため）。将来のマルチユーザー対応時にJWT認証を追加する予定だが、APIの設計変更は最小限にとどめる。

---

*WISE v2 API.md v1.0 — 2026-06-30*
