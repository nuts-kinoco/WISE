# WISE v2 Pipeline.md (v2.0)

> **本書はv1.0からv2.0への更新である。**  
> 変更の主目的：StorageFormat検出パイプライン追加（FB③）、ICoverProvider統合パイプライン追加（FB④）、ArchiveIndex構築パイプライン追加（Comic対応）、IMediaViewer選択フロー（FB⑧）、FTS5 IndexUpdateJobパイプライン（FB⑦）、ReadingHistory更新フロー（FB②）。

前提資料：**Architecture.md v2.0**、**Domain.md v2.0**、**Database.md v2.0**

---

# 1. Pipelineとは

## 責務と設計思想

Pipelineとは、WISE内で実行される「すべての状態変化の伝播」と「時間のかかる処理（非同期処理）」を安全かつ確実にオーケストレーションする仕組みである。

設計思想の根幹は **「ユーザーの操作（UI）をブロックしないこと」** と **「部分的失敗を許容し、全体を止めないこと」** にある。

## v2での追加コンポーネント

| コンポーネント | 役割 | 関連FB |
|---|---|---|
| StorageFormatDetector | Assetのコンテナ形式を自動検出 | FB③ |
| AssetRoleAssigner | ImportJob内でAssetのRoleを確定 | FB① |
| CoverPipeline | ICoverProviderチェーンでカバー取得 | FB④ |
| ArchiveIndexPipeline | IArchiveReaderでページリスト構築 | FB③ |
| IndexUpdatePipeline | FTS5仮想テーブル更新 | FB⑦ |
| ViewerRouter | IMediaViewerでビューワーを選択 | FB⑧ |
| ReadingHistoryFlush | フロントエンド→DBへのdebounce更新 | FB② |

---

# 2. メインパイプライン（v2更新）

ファイル発見からGallery表示・Viewer選択・FTS5更新に至る完全フロー。

```mermaid
flowchart TD
    subgraph Input Phase
        PF[PhysicalFile 発見] --> Asset[Asset 生成\nwork_id=NULL]
        Asset --> SFD[StorageFormat 検出\nMIME/拡張子から判定]
    end

    subgraph Identification Phase
        SFD --> Norm[Normalizer\nファイル名正規化]
        Norm --> ID[Identifier Resolver\nEvidence収集・Confidence算出]
        ID --> WorkDec{Work決定}
        WorkDec -- 既存Work --> ExWork[既存Work に紐付け]
        WorkDec -- 新規 --> NewWork[新規Work生成\nmedia_type設定]
    end

    subgraph Role Assignment Phase
        ExWork --> RoleA[AssetRole 確定\nImport処理]
        NewWork --> RoleA
    end

    subgraph Sync Output Phase
        RoleA --> Gal[Gallery 即時表示\nMediaDisplayProfile適用]
    end

    subgraph Async Job Phase
        RoleA -- WorkCreated Event --> JobQ[(Job Queue)]
        JobQ --> MetaJob[MetadataFetchJob\nProvider別並列取得]
        JobQ --> HashJob[HashCalcJob\nSHA256算出]
        JobQ --> CoverJob[CoverExtractJob\nICoverProvider Chain]
        JobQ --> ArchJob[ArchiveIndexJob\nIArchiveReader\nComic/Book のみ]
    end

    subgraph Post-Processing Phase
        MetaJob -- MetadataUpdated --> IdxJob[IndexUpdateJob\nFTS5更新]
        MetaJob -- MetadataUpdated --> RuleEng[Rule Engine]
        MetaJob -- MetadataUpdated --> EvtLog[Event Log]
        CoverJob --> CoverCache[(Cover Cache)]
        ArchJob --> PageList[(Page List\nJSON Cache)]
    end
```

---

# 3. StorageFormat検出パイプライン（v2新規）

ファイル発見時にStorageFormatを自動検出し、ASSETに記録する。

```mermaid
flowchart LR
    File[ファイルパス] --> Ext[拡張子チェック]
    Ext -- .zip/.cbz --> Arch[Archive]
    Ext -- .rar/.cbr --> Arch
    Ext -- .pdf --> Pdf[Pdf]
    Ext -- .epub --> Epub[Epub]
    Ext -- ディレクトリ --> Folder[Folder]
    Ext -- .mp4/.mkv/.avi --> SF[SingleFile]
    Ext -- その他 --> SF
    Arch --> MIME[MIME署名検証\n先頭バイトで確認]
    Pdf --> MIME
    MIME --> SetSF[ASSET.storage_format 設定]
    Epub --> SetSF
    Folder --> SetSF
    SF --> SetSF
```

**StorageFormat検出ルール：**

| 優先度 | 判断方法 | 説明 |
|---|---|---|
| 1 | MIMEシグネチャ（バイナリ先頭） | 最も信頼性が高い |
| 2 | 拡張子 | MIMEが不明な場合 |
| 3 | ディレクトリ判定 | パスがディレクトリであれば `Folder` |
| 4 | デフォルト | 判定不能の場合は `SingleFile` |

---

# 4. AssetRole確定パイプライン（v2新規）

AssetがWorkに紐付けられた後、WorkのMediaTypeとAssetの物理属性からRoleを確定する。

```mermaid
flowchart TD
    Asset[Asset\nstorage_format確定済み] --> MT{Work.MediaType}
    MT -- Video --> VR{storage_format?}
    VR -- SingleFile --> VideoRole[role=Video]
    VR -- Archive/Folder --> AttRole[role=Attachment]
    VR -- Other --> SampleRole{ファイル名にsample含む?}
    SampleRole -- YES --> SR[role=Sample]
    SampleRole -- NO --> VR2[role=Attachment]

    MT -- Comic --> CR{storage_format?}
    CR -- Archive/Folder/Pdf --> ArchRole[role=Archive]
    CR -- SingleFile 画像 --> ImgRole[role=Image]

    MT -- Book --> BR{storage_format?}
    BR -- Epub/Pdf --> BookRole[role=Archive]
    BR -- Other --> BAtt[role=Attachment]

    VideoRole --> Done[ASSET.role 確定]
    ArchRole --> Done
    ImgRole --> Done
    BookRole --> Done
    SR --> Done
    AttRole --> Done
    BAtt --> Done
```

**カバー画像のRole確定：**
- ファイル名に `cover`, `poster`, `fanart`, `thumb` を含む画像ファイル → `CoverPortrait` または `CoverLandscape`（アスペクト比で判断）
- アスペクト比 > 1.0（横長）→ `CoverLandscape`
- アスペクト比 <= 1.0（縦長）→ `CoverPortrait`

---

# 5. ICoverProvider パイプライン（v2新規）

CoverExtractJobが実行されると、ICoverProvider Chain of Responsibilityが動作する。

```mermaid
sequenceDiagram
    participant Job as CoverExtractJob
    participant CS as CoverService
    participant MCP as MetadataCoverProvider
    participant ACP as ArchiveCoverProvider
    participant VTP as VideoThumbnailProvider
    participant DP as DefaultCoverProvider
    participant Cache as COVER_CACHE

    Job->>CS: GetCoverAsync(work)
    CS->>MCP: GetCoverAsync(work)  [Priority:100]
    alt MetadataField.cover_url が存在
        MCP-->>CS: CoverResult（URLダウンロード）
        CS->>Cache: UPSERT（provider_name=MetadataCoverProvider）
        CS-->>Job: Done
    else cover_url なし
        MCP-->>CS: null
        alt MediaType = Comic
            CS->>ACP: GetCoverAsync(work)  [Priority:80]
            ACP->>ACP: IArchiveReader.GetPageStreamAsync(path, 0)
            ACP-->>CS: CoverResult（1ページ目画像）
            CS->>Cache: UPSERT
        else MediaType = Video
            CS->>VTP: GetCoverAsync(work)  [Priority:60]
            VTP->>VTP: ffmpeg -ss 00:01:00 -frames:v 1 ...
            VTP-->>CS: CoverResult（フレーム画像）
            CS->>Cache: UPSERT
        else すべて失敗
            CS->>DP: GetCoverAsync(work)  [Priority:0]
            DP-->>CS: CoverResult（プレースホルダー）
        end
    end
```

**キャッシュ方針：**
- キャッシュパス：`{AppData}/WISE/covers/{workId}/{provider}.jpg`
- MetadataCoverProviderのキャッシュは `expires_at = null`（無期限）。ただしMetadataが更新されたら再取得をJobに投入
- ArchiveCoverProvider/VideoThumbnailProviderのキャッシュは `expires_at = null`（ファイルが変わらない限り有効）

---

# 6. ArchiveIndex パイプライン（v2新規、Comic/Book専用）

```mermaid
flowchart LR
    Job[ArchiveIndexJob] --> AR{IArchiveReader選択\nstorage_format基準}
    AR -- Archive\n.zip/.cbz/.rar/.cbr --> ZR[ZipArchiveReader\nまたはRarArchiveReader]
    AR -- Pdf --> PR[PdfArchiveReader]
    AR -- Epub --> ER[EpubArchiveReader]
    AR -- Folder --> FR[FolderArchiveReader]
    ZR --> Pages[ArchivePage一覧\n[{index, name, size}]]
    PR --> Pages
    ER --> Pages
    FR --> Pages
    Pages --> Cache[ページリストをJSON形式でキャッシュ]
    Pages --> PCnt[METADATA_FIELD.page_count 更新\nis_primary=true, provider=system]
```

**ArchiveIndexJobの投入タイミング：**
- AssetRole確定時（`role=Archive` と確定された直後）
- アーカイブファイルの置き換えを検出した時（SHA256が変わった時）

**パフォーマンス考慮：**
- ページリストはJSONキャッシュに保存し、ビューワー初回開時のリスト生成をスキップ
- 1ページ目の画像はCoverExtractJobが別途取得（ArchiveIndexJobはページリストのみ、画像バイト列は持たない）

---

# 7. FTS5 IndexUpdate パイプライン（v2新規）

```mermaid
flowchart LR
    Evt[MetadataUpdated Event] --> Job[IndexUpdateJob]
    Job --> DB[(METADATA_FIELD)]
    DB --> FTS[METADATA_FTS\nFTS5仮想テーブル]
    FTS --> Query[全文検索クエリ\nMediaType非依存]
```

**更新方式：**
```sql
-- コンテンツテーブルモードを使用（rebuild）
INSERT INTO METADATA_FTS(METADATA_FTS) VALUES('rebuild');
-- または差分更新（delete + insert）
INSERT INTO METADATA_FTS(METADATA_FTS, rowid, value)
  VALUES('delete', :oldRowid, :oldValue);
INSERT INTO METADATA_FTS(rowid, work_id, field_name, value)
  VALUES(:rowid, :workId, :fieldName, :value);
```

**設計原則（FB⑦）：**
- MediaType依存のインデックス分割は行わない
- 全てのMetadataField（`is_primary = true`）がFTS5の対象

---

# 8. IMediaViewer選択フロー（v2新規）

ビューワー起動時にViewerRouterServiceがIMediaViewerを選択し、フロントエンドに `ViewerRoute` を返す。

```mermaid
sequenceDiagram
    participant UI as Frontend
    participant API as WorksController
    participant VR as ViewerRouterService
    participant DB as Database

    UI->>API: GET /api/works/{id}/viewer-info
    API->>DB: Work取得（media_type, assets）
    API->>VR: GetViewer(work.MediaType)
    VR-->>API: IMediaViewer（ViewerRoute, Capabilities）
    API-->>UI: { viewerRoute: "/viewer/comic",\n  capabilities: { supportsPageNavigation: true, ... },\n  resumePosition: { pageNumber: 42 } }
    UI->>UI: router.push(viewerRoute + "?workId=" + id)
```

**UIの責務：**
- `viewerRoute` に従ってルーティングするだけ
- `capabilities` を読んでボタン（速度変更・ブックマーク等）の表示/非表示を制御
- if(mediaType === "Video") などのMediaType依存コードを書かない

---

# 9. ReadingHistory更新フロー（v2新規）

```mermaid
sequenceDiagram
    participant Viewer as フロントエンドViewer
    participant LS as localStorage
    participant API as ReadingHistoryController
    participant DB as READING_HISTORY

    Viewer->>LS: 進捗をリアルタイム保存\n（wise-reader-{workId}-{deviceId}）
    loop ページめくり・時間経過
        Viewer->>LS: 更新（debounce: 5秒）
    end
    Viewer->>API: PUT /api/works/{id}/reading-history\n（ページクローズ時 / 5秒interval）
    API->>DB: UPSERT READING_HISTORY\n（work_id, device_id）UNIQUE
    DB-->>API: 200 OK
```

**デバイスID生成：**
```javascript
// localStorage初回起動時に生成・保持
const deviceId = localStorage.getItem('wise-device-id')
  ?? crypto.randomUUID();
localStorage.setItem('wise-device-id', deviceId);
```

**フラッシュのタイミング：**
- ページめくり後5秒（debounce）
- ビューワーunmount時（`beforeunload` イベント）
- アプリバックグラウンド遷移時（`visibilitychange` → hidden）

---

# 10. Job System（v2更新）

## JobType一覧（v2追加分）

| JobType | 説明 | 投入トリガー | 新規/既存 |
|---|---|---|---|
| `MetadataFetch` | Metadata取得 | WorkCreated | 既存 |
| `HashCalc` | SHA256計算 | AssetAssociated | 既存 |
| `Thumbnail` | サムネイル生成 | WorkCreated（Video） | 既存 |
| `CoverExtract` | ICoverProvider Chain実行 | WorkCreated / MetadataUpdated | **新規** |
| `ArchiveIndex` | IArchiveReaderでページリスト構築 | AssetRole=Archive確定時 | **新規** |
| `IndexUpdate` | FTS5インデックス更新 | MetadataUpdated | **新規** |
| `MediaInfo` | 動画MediaInfo取得 | AssetAssociated（Video） | 既存 |
| `Duplicate` | 重複検出 | HashCalc完了後 | 既存 |

## 優先度設計

```
100: ユーザーが明示的に指示したJob（手動再取得等）
 80: CoverExtract（Gallery表示に影響）
 70: MetadataFetch
 60: ArchiveIndex（ビューワー起動に影響）
 50: IndexUpdate（検索に影響、少し遅延許容）
 40: HashCalc
 30: Thumbnail
 20: MediaInfo
 10: Duplicate
```

---

# 11. Provider Pipeline（v2更新）

Metadata取得において、WorkのMediaTypeに応じたProviderのみを並列実行する。

```mermaid
sequenceDiagram
    participant Worker
    participant PM as ProviderManager
    participant MF as MediaFilter
    participant P1 as FANZA API（Video専用）
    participant P2 as DLSite（Comic専用）
    participant P3 as LocalNFO（All対応）
    participant MS as MetadataService

    Worker->>PM: FetchMetadata(work)
    PM->>MF: FilterByMediaType(work.MediaType)
    alt MediaType = Video
        MF-->>PM: [FANZA, JavBus, LocalNFO]
        par 並列取得
            PM->>P1: request
            P1-->>PM: Success
        and
            PM->>P3: request
            P3-->>PM: Success
        end
    else MediaType = Comic
        MF-->>PM: [DLSite, Getchu, ComicInfoXml, LocalNFO]
        par 並列取得
            PM->>P2: request
            P2-->>PM: Success
        and
            PM->>P3: request
            P3-->>PM: Success
        end
    end
    PM->>MS: 取得結果を渡す（部分成功でもOK）
    MS->>MS: Conflict Resolution
    MS-->>Worker: MetadataUpdated
```

---

# 12. Event Pipeline

```mermaid
flowchart LR
    subgraph Events
        E1((AssetDetected))
        E2((WorkCreated))
        E3((MetadataUpdated))
        E4((RuleExecuted))
        E5((AssetRoleAssigned))
    end

    subgraph Subscribers
        S_ID[Identifier Resolver]
        S_Job[Job Scheduler]
        S_Cover[CoverExtractJob]
        S_Arch[ArchiveIndexJob]
        S_Rule[Rule Engine]
        S_Search[IndexUpdateJob]
        S_Hist[Event Log Service]
    end

    E1 --> S_ID
    E2 --> S_Job
    E2 --> S_Cover
    E3 --> S_Rule
    E3 --> S_Search
    E5 --> S_Arch
    E1 & E2 & E3 & E4 & E5 --> S_Hist
```

---

# 13. Error Recovery（v2更新）

| 障害シナリオ | 回復戦略 |
|---|---|
| **ネットワーク障害（Metadata取得）** | Job Retry（Exponential Backoff）。最大リトライ超過でDeadLetter |
| **Providerサイト仕様変更** | Circuit Breaker発動。他Providerにフォールバック |
| **IArchiveReaderが対応外の形式** | ArchiveIndexJob失敗。role=Archive のみ確定し、ページリストなしでGallery表示 |
| **ICoverProvider全滅** | DefaultCoverProviderがプレースホルダーを返す（エラーにしない） |
| **FTS5更新失敗** | IndexUpdateJobを再投入。FTS5は古い状態のまま検索を継続（Eventual Consistency） |
| **Identifier解決失敗** | Orphaned Asset として保留。ユーザーがDiagnostic画面から手動解決 |
| **Jobワーカー強制終了** | `status = 'Running'` でタイムアウトしたJobを監視プロセスが `Failed` に戻してキューに復帰 |

---

# 14. 採用しなかった設計

| 不採用の設計案 | 不採用理由 |
|---|---|
| 完全な同期処理（MetadataまでUIをブロック） | ネットワークが遅いとアプリ全体がフリーズ |
| ComicCoverExtractor（MediaType固有クラス直接呼び出し） | Plugin追加時にコア変更が必要。Strategy化（ICoverProvider）を採用（FB④） |
| MediaType別のJob Pipeline分岐 | `if(mediaType == Comic)` がPipeline層に入るとPlugin化に耐えられない |
| ReadingHistoryのリアルタイムDB更新（毎ページめくりごと） | SQLiteのロック競合リスク。debounce + localStorageで対応（FB②） |

---

*WISE v2 Pipeline.md v2.0 — 2026-06-30*
