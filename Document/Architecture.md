# WISE v2 Architecture.md (v2.0)

> **本書はv1.1からv2.0へのメジャー更新である。**  
> 変更の主目的：Comic/Doujinライブラリ拡張対応（MediaType抽象化）、AssetRole導入、ICoverProvider戦略化、IMediaViewer DI化、MediaDisplayProfile導入、FTS5全文検索、Plugin-readyアーキテクチャ、引き算の美学によるDisplay Customization。

---

## 1. 設計思想

### 1.1 WISEとは

WISEは「AV・同人誌・書籍・画像集など複数のメディアタイプ」を一元管理するパーソナルメディアライブラリシステムである。ユーザーが所有するファイルを「作品（Work）」という概念で整理し、メタデータ取得・検索・閲覧・整理を統合的に提供する。

### 1.2 最重要思想：Source of Truth

**DBがすべての判断基準。ファイルシステムは「DBが指す先」に過ぎない。**

この思想から導かれる設計上の決定：

| 思想 | 実装上の表現 |
|---|---|
| ファイルが消えてもWorkは消えない | `ASSET.status = 'missing'` でWork自体は保持 |
| ファイル名は手がかりに過ぎない | 真の同一性判断は `ASSET.sha256` と `WORK.primary_identifier` |
| DBに存在しないWorkはWISEでは存在しない | ファイルスキャン結果はDB登録完了をもって有効 |
| 全操作の根拠はDBに残る | `EVENT_LOG` がすべての状態変化の唯一の監査証跡 |

> ⚠️ **既知の逸脱（未解決の設計判断、P4監査で指摘）**:
> 1. `WorksController.DeleteWork`（実装は`WorkFileUseCase.DeleteWorkAsync`）は
>    Work行と関連レコードをDBから**物理削除**する。「ファイルが消えてもWorkは消えない」の
>    裏返しである「WorkをDBから消す」操作自体は本原則と矛盾しないが、`Document/API.md`は
>    これを誤って「Soft Delete」と記載していた（本改訂で修正済み）。Soft Delete化するかどうかは
>    別途の設計判断として残っている。
> 2. `userMemo`/`userFavorite`/`userRating`が`metadata.json`（動画ファイル横のファイル）にも
>    二重書き込みされており（`WorkUserDataUseCase`, `DuplicateResolveUseCase`の`WorkMetadataJsonHelper`
>    経由）、一部の読み取り（`GetUserMemo`）はDBではなくこの`metadata.json`から行われる。
>    「ファイルシステムはDBが指す先に過ぎない」原則から見ると、ファイル側を実質的な正データの
>    一部として読んでいる点が逸脱している。意図的なポータビリティ対応である可能性はあるが、
>    設計判断としては未整理。

### 1.3 Work中心原則

すべてのビジネス概念（Metadata、Evidence、Collection、EventLog、Job、ReadingHistory）はWorkに関連付けられる。WorkはWISEの宇宙における重力の中心である。

### 1.4 引き算の美学（Product Constitution最優先原則）

WISEのUXは「足す」のではなく「引く」ことで成立する。

- Gallery表示フィールドはON/OFFできる（MediaDisplayProfile）
- ビューワーはMediaTypeに応じてDIで差し替わる（IMediaViewer）
- カバー取得はMediaTypeに応じてStrategyが切り替わる（ICoverProvider）
- メディア種別依存のif/elseをUIに書かない（MediaDisplayProfile経由）

---

## 2. システム全体構成

### 2.1 レイヤー構成

```
┌────────────────────────────────────────────┐
│  Presentation Layer                        │
│  Next.js 16 (App Router)                   │
│  Gallery / Detail / Viewer / Organize      │
└────────────────┬───────────────────────────┘
                 │ HTTP / REST API
┌────────────────┴───────────────────────────┐
│  API Layer (ASP.NET Core 8)                │
│  Controllers / UseCases                    │
│  Middleware: Auth, Logging, Error          │
└────────────────┬───────────────────────────┘
                 │
┌────────────────┴───────────────────────────┐
│  Application Layer                         │
│  Services / Job Handlers / Use Cases       │
│  MetadataService / ConflictResolver        │
│  CoverService / ViewerRouter               │
└────────────────┬───────────────────────────┘
                 │
┌────────────────┴───────────────────────────┐
│  Domain Layer                              │
│  Entities: Work / Asset / MetadataField    │
│  Value Objects: AssetRole / StorageFormat  │
│  Interfaces: IMetadataProvider             │
│              ICoverProvider                │
│              IMediaViewer                  │
│              IArchiveReader                │
│  Models: MediaDisplayProfile               │
└────────────────┬───────────────────────────┘
                 │
┌────────────────┴───────────────────────────┐
│  Infrastructure Layer                      │
│  Repositories / EF Core / SQLite           │
│  Providers: FANZA / JavBus / DLSite        │
│  Cover: MetadataCover / ArchiveCover       │
│         VideoThumbnail / PdfCover          │
│  Readers: ZipArchiveReader / PdfReader     │
└────────────────────────────────────────────┘
```

### 2.2 Work中心のDB関係

```
Work
├── Asset（role: Video / Archive / Image / CoverPortrait / ...）
├── Evidence（Identifier解決の根拠）※未実装。`IEvidenceProvider`インターフェースと
│                                    `ProviderDiagnostic`エンティティが近い概念として存在するが、
│                                    本節が指す永続化されたEvidenceエンティティ自体は存在しない
├── MetadataField（作品情報の各フィールド、FTS5対象。ただし§4.9注記の通りFTS5は検索に未接続）
├── Job（非同期処理の実行単位）
├── EventLog（状態変化履歴）
├── ReadingHistory（読書進捗 — Work:N, Device:N → 独立エンティティ）
└── CollectionWork → Collection（Author/Circle/Series/Maker/Playlist...）
```

---

## 3. MediaType抽象化

### 3.1 MediaType一覧

WISEが管理するメディア種別を `MediaType` として定義する。すべてのMediaTypeは同じドメインモデルを共有し、差異は抽象化されたインターフェース群（ICoverProvider, IMediaViewer, IIdentifierStrategy）を介して吸収する。

| MediaType | 値 | 代表的なファイル形式 | 識別子例 |
|---|---|---|---|
| Video | 1 | MP4, MKV, AVI | `FANZA-ABP-123`, `FC2-PPV-456` |
| Comic | 2 | ZIP, CBZ, RAR, CBR, 画像フォルダ | `DLSite-RJ123456`, `circle-title-hash` |
| Book | 3 | EPUB, PDF（テキスト主体） | `ISBN-978-...`, `publisher-title-hash` |
| PhotoBook | 4 | ZIP, PDF（写真集） | `FANZA-BOOK-...` |
| ImageCollection | 5 | 画像フォルダ | `folder-hash` |
| Audio | 6 | MP3, FLAC, AAC | `circle-title-hash` |

### 3.2 MediaType依存のStrategy化

MediaTypeによって異なる実装を要する箇所はすべてStrategyパターンで抽象化する。UIは `MediaDisplayProfile` を読み取るだけでif/elseを持たない。

```
MediaType
    └──→ IIdentifierStrategy     （識別子抽出の戦略）
    └──→ IMetadataProvider       （対応Providerの宣言 via SupportedMediaTypes）
    └──→ ICoverProvider          （カバー画像取得の戦略）
    └──→ IMediaViewer            （再生/閲覧の戦略）
    └──→ MediaDisplayProfile     （Gallery/Detail表示設定）
    └──→ IArchiveReader          （ページストリーミングの戦略 — Comic/Book/PDF）
```

### 3.3 SupportedMediaTypes

`IMetadataProvider` は `IReadOnlyList<MediaType> SupportedMediaTypes` を公開する。

- Videoのみ対応するProvider（FANZA, JavBus）は `[Video]` を返す
- Comic対応Provider（DLSite, Getchu）は `[Comic, Book]` を返す
- LocalNFO等汎用Providerは `[Video, Comic, Book, ...]` を返す

Provider ManagerはWorkのMediaTypeでProviderをフィルタリングし、対応Providerのみに問い合わせる。

---

## 4. コアコンポーネント

### 4.1 Asset とAssetRole

> ⚠️ **既知の設計負債（未解消）**: 本節は「AssetRoleがAssetTypeを置き換える」という v2 設計意図を
> 記述しているが、実装では `Asset` エンティティに `AssetType`（`AssetType.cs`: Unknown/Video/
> PortraitCover/LandscapeCover/Thumbnail/SampleImage/Subtitle/PageHtml/MetadataJson/Nfo の10種）と
> `AssetRole`（本節の表、14種）が**両方存在し、両プロパティが並存**している。コントローラー層
> （`WorksController`/`CollectionsController`/`DuplicatesController` 等のカバー画像フィルタ等）は
> 主に `AssetType` を参照し、`AssetRole` はインポート時（`ExecuteImportJobUseCase`等）に設定される
> ものの一部のCoverProviderでしか参照されていない。どちらに収斂させるかは未決定の設計判断であり、
> 新規コードでは既存コントローラーとの整合を優先して **`AssetType`** を使うこと。

AssetはPhysicalFileのDB上の表現である。各AssetはWorkの中で「どういう役割を担うか」を `AssetRole` で表現する（設計意図。実装上の実態は上記注記を参照）。

**AssetRole一覧：**

| Role | 意味 | 主なMediaType |
|---|---|---|
| `Video` | 本編動画 | Video |
| `Archive` | ZIP/CBZ/RAR等のアーカイブファイル | Comic, Book |
| `Image` | 単体画像ファイル（コレクション等） | ImageCollection |
| `CoverPortrait` | 縦向きカバー画像 | Video, Comic, Book |
| `CoverLandscape` | 横向きカバー画像 | Video |
| `Thumbnail` | サムネイル | Video, Comic |
| `AnimatedThumbnail` | アニメーションサムネイル | Video |
| `Sample` | サンプル画像 / サンプル動画 | Video |
| `Subtitle` | 字幕ファイル | Video |
| `NFO` | NFOファイル | Video, Comic |
| `MetadataFile` | メタデータファイル（ComicInfo.xml等） | Comic |
| `Attachment` | 添付ファイル | all |
| `Preview` | プレビュー | Comic |

> **AssetRoleはAssetのタイプではなく役割（Role）である。**  
> 同一ファイルが別のWorkでは別のRoleを持つことはない。RoleはWorkの文脈でのAssetの機能を表す。

### 4.2 StorageFormat（コンテナ形式）

Assetがコンテンツをどのようにパッケージするかを表す。AssetRoleがAssetの「機能」を表すのに対し、StorageFormatは「物理的な格納形式」を表す。

| StorageFormat | 対象 | 備考 |
|---|---|---|
| `Archive` | ZIP, CBZ, RAR, CBR | IArchiveReaderで展開 |
| `Folder` | 画像フォルダ | ファイルシステム直接参照 |
| `Pdf` | PDF | IArchiveReader/PdfReaderで処理 |
| `Epub` | EPUB | EpubReaderで処理 |
| `SingleFile` | MP4, MKV, MP3等 | 単一ファイル（Default） |

StorageFormatはコミックビューワー（IArchiveReader）の選択に使用される。

### 4.3 Identifier Resolver

ファイル名・ハッシュ・外部Evidenceを積み上げてConfidenceスコアを算出し、対応するWorkを確定する。

- Confidence閾値：60点（DB管理、変更可能）
- Evidence永続化：DBに保存し、Diagnostic画面で推論過程を可視化
- Evidence例：ファイル名パターン一致（+30）、FANZA品番確認（+40）、DLSite確認（+40）

### 4.4 ICoverProvider（カバー画像取得の戦略）

カバー画像の取得元はMediaTypeとAsset状態に応じて動的に選択される。固定のComicCoverExtractorではなく、Strategy Chain として実装する。

```csharp
interface ICoverProvider
{
    IReadOnlyList<MediaType> SupportedMediaTypes { get; }
    int Priority { get; }
    Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct);
}
```

**実装一覧（優先度降順）：**

| 実装 | Priority | 動作 |
|---|---|---|
| `MetadataCoverProvider` | 100 | MetadataFieldの `CoverPortrait` / `CoverLandscape` URLを使用 |
| `ArchiveCoverProvider` | 80 | ZIP/CBZの1ページ目画像を抽出してキャッシュ |
| `VideoThumbnailProvider` | 60 | ffmpegでフレーム抽出してキャッシュ |
| `PdfCoverProvider` | 60 | PDFの1ページ目をレンダリングしてキャッシュ |
| `FolderCoverProvider` | 40 | 画像フォルダの先頭画像を使用 |
| `DefaultCoverProvider` | 0 | プレースホルダー画像を返す |

CoverServiceはChain of Responsibilityでこれらを順に試行し、最初に非nullを返したものを採用する。

### 4.5 IMediaViewer（メディア閲覧の戦略）

MediaTypeに応じた閲覧コンポーネントをDIで提供する。UIはIMediaViewerを参照するだけで、MediaType依存のif/elseを含まない。

```csharp
interface IMediaViewer
{
    IReadOnlyList<MediaType> SupportedMediaTypes { get; }
    ViewerCapabilities Capabilities { get; }
    string ViewerRoute { get; }  // Frontend route: "/viewer/video", "/viewer/comic", ...
}

// 設計時点の想定。実装（WorksController.GetViewerInfo が返す capabilities）は
// フィールド構成が異なる: SupportsPageNavigation / SupportsDoublePage / SupportsPrefetch /
// SupportsTimeSeek / SupportsResume の5つ（Zoom/PlaybackSpeed/Bookmarkは未実装、
// 代わりにDoublePage/Prefetch/TimeSeekが実装されている）
record ViewerCapabilities(
    bool SupportsResume,
    bool SupportsPageNavigation,
    bool SupportsZoom,
    bool SupportsPlaybackSpeed,
    bool SupportsBookmark
);
```

**実装一覧：**

| 実装 | MediaType | ViewerRoute |
|---|---|---|
| `VideoViewer` | Video | `/viewer/video` |
| `ComicViewer` | Comic, PhotoBook, ImageCollection | `/viewer/comic` |
| `EpubViewer` | Book（EPUB） | `/viewer/epub` |
| `PdfViewer` | Book（PDF）, PhotoBook（PDF） | `/viewer/pdf` |

> **Capabilities vs DI：** IMediaViewerはDI（実装切り替え）とCapabilities（機能照会）を両立する。  
> UIはCapabilitiesを読んでボタンの表示/非表示を決定し、DI登録でViewerの実体を差し替える。

### 4.6 MediaDisplayProfile

GalleryおよびDetailページの表示設定を定義するProfileオブジェクト。MediaTypeごとにデフォルトProfileが存在し、ユーザーはフィールドON/OFFをカスタマイズできる。

```csharp
record MediaDisplayProfile(
    MediaType MediaType,
    CoverOrientation DefaultCoverOrientation,  // Portrait / Landscape
    IReadOnlyList<DisplayField> GalleryFields,  // 表示するフィールドの順序・ON/OFF
    IReadOnlyList<DisplayField> DetailFields,
    string DefaultSort
);

record DisplayField(string FieldName, bool IsVisible, string? Label);
```

**デフォルトProfile例：**

| MediaType | CoverOrientation | Gallery主要フィールド |
|---|---|---|
| Video | Landscape | Title, Actress, Maker, Duration |
| Comic | Portrait | Title, Circle, Author, PageCount |
| Book | Portrait | Title, Author, Publisher, PageCount |

UI（Gallery, WorkCard）はMediaDisplayProfileを参照し、if/elseなしで表示フィールドを決定する。

### 4.7 IArchiveReader（アーカイブ読み取りの戦略）

Comic/Book閲覧時にアーカイブファイルからページ画像をストリームするインターフェース。ファイル展開（解凍）は行わない。

```csharp
interface IArchiveReader
{
    IReadOnlyList<StorageFormat> SupportedFormats { get; }
    Task<int> GetPageCountAsync(string path, CancellationToken ct);
    Task<Stream> GetPageStreamAsync(string path, int pageIndex, CancellationToken ct);
    Task<IReadOnlyList<string>> GetPageNamesAsync(string path, CancellationToken ct);
}
```

**実装一覧：**

| 実装 | StorageFormat |
|---|---|
| `ZipArchiveReader` | Archive（ZIP, CBZ） |
| `RarArchiveReader` | Archive（RAR, CBR） |
| `PdfArchiveReader` | Pdf |
| `EpubArchiveReader` | Epub |
| `FolderArchiveReader` | Folder |

### 4.8 Job Queue（DB永続化キュー）

時間のかかる処理をすべて非同期Jobとして管理する。JobはDBに永続化され、アプリ再起動後も継続される。

**JobType一覧（設計時点の想定。実装との対応は下表を参照）：**

- `MetadataFetch` — Metadata取得
- `HashCalc` — SHA256計算
- `Thumbnail` — サムネイル生成
- `CoverExtract` — ICoverProviderによるカバー抽出（新規）
- `ArchiveIndex` — アーカイブページリスト構築（新規）
- `MediaInfo` — 動画MediaInfo取得
- `IndexUpdate` — FTS5インデックス更新（新規）
- `Duplicate` — 重複検出

> ⚠️ **実装との乖離**: 実際に生成される `Job.JobType` 文字列は
> `FetchMetadata` / `Import` / `RebuildFts` / `System` / `WatchFolder` の5種のみ。
> 上記一覧の個別JobType（HashCalc/Thumbnail/CoverExtract/ArchiveIndex/MediaInfo/IndexUpdate/
> Duplicate）は独立したJobとしては実装されておらず、`FetchMetadata`ジョブ内の処理として
> まとめて実行されるか、または未実装（重複検出はJob化されておらずAPI呼び出し時に同期実行）。

### 4.9 FTS5全文検索

SQLite FTS5を使用して、全MetadataFieldを横断的に検索できる仮想テーブルを管理する（設計意図）。

- 検索対象：`METADATA_FIELD.value`（全フィールド）
- MediaTypeによる検索制限は行わない（FB⑦原則）
- FTS5仮想テーブル：`METADATA_FTS` (work_id, field_name, value)
- IndexUpdateJobがMetadataUpdatedイベントをトリガーに更新

> ⚠️ **未解決の設計判断（実装との乖離）**: FTS5仮想テーブル自体は`Program.cs`で作成されており
> `RebuildFts`ジョブによる更新機構もあるが、実際の検索エンドポイント（`WorksController`一覧の
> `q`パラメータ、`WorksQueryService.GetListAsync`）はFTS5を使わず、`Title`/`Maker`/`Actress`/
> `ActressTag`/`Label`/`Genre`/`Tag`の8フィールドに対する`LIKE`（`.Contains()`）の相関サブクエリで
> 実装されている。FTS5インデックス更新のコストだけ払って検索には使っていない状態であり、
> 「FTS5を検索に接続する」か「FTS5機構自体を廃止しLIKE方式を正式な設計として採用する」かの
> 判断が必要（P4監査で指摘、未決定）。

### 4.10 Collection（クロスメディア対応強化）

Collectionは単一テーブルで全種別を管理し、v2でクロスメディア対応する（設計意図）。

> ⚠️ **実装との乖離**: 実装（`Collection`エンティティ）には`Type`という概念自体が存在せず、
> `Name`/`Description`/`Items`のみを持つ。下記の8種のCollectionTypeは未実装で、
> 現状は「ユーザーが任意に作る手動プレイリスト」1種類のみ利用可能。

**v2追加CollectionType（未実装・将来拡張の想定）：**

| type | 意味 | クロスメディア |
|---|---|---|
| `Author` | 作者（Video女優 / Comic作家 共通） | ✓ |
| `Circle` | サークル（主にComic/Doujin） | ✓ |
| `Person` | 出演者（作家・女優・監督などを横断） | ✓ |
| `Series` | シリーズ（Video・Comic共通） | ✓ |
| `Maker` | メーカー・レーベル | ✓ |
| `Favorite` | お気に入り | ✓ |
| `Playlist` | 手動プレイリスト | ✓ |
| `SmartFolder` | 動的抽出 | ✓ |

---

## 5. Plugin-Readyアーキテクチャ

v2では実装しないが、将来のPlugin化に耐えられる構造を今から維持する。

### 5.1 Plugin拡張ポイント

以下のインターフェースはPlugin化の主要拡張ポイントである。実装はInfrastructure層に置き、DIコンテナで登録する。Plugin SDKが整備された時点で、外部アセンブリからの登録を可能にする。

```
IMetadataProvider    → サードパーティMetadata取得Provider
ICoverProvider       → カバー画像取得Provider
IMediaViewer         → カスタムビューワー
IArchiveReader       → カスタムアーカイブ形式
IIdentifierStrategy  → カスタム識別子抽出戦略
```

### 5.2 コードの禁止事項（Plugin-Readiness維持のため）

- ❌ `switch(mediaType)` / `if(mediaType == Video)` をApplication層・Domain層に書かない
- ❌ Provider名をコードにハードコードしない（DBのPROVIDER.nameを参照する）
- ❌ ビューワーのルートをApplication層にハードコードしない（IMediaViewer.ViewerRoute経由）
- ✓ 全てのMediaType依存はDI・Strategy・Profileで抽象化する

---

## 6. インフラストラクチャ

### 6.1 技術スタック

| 区分 | 技術 | バージョン |
|---|---|---|
| Backend API | ASP.NET Core | 8.0 |
| ORM | Entity Framework Core | 8.0 |
| Database | SQLite（開発・個人用途） | 3.38+ |
| Full-Text Search | SQLite FTS5 | 組み込み |
| Frontend | Next.js | 16（`package.json`実態。本書は当初15と記載していた） |
| UI Framework | Tailwind CSS | 4.x |
| State Management | Zustand | - |
| Virtual Scroll | @tanstack/react-virtual | - |

### 6.2 SQLiteの運用方針

- WALモード必須（Job QueueのロックとUIの競合を防ぐ）
- FTS5仮想テーブルはメインDB内に保持（別ファイル分離は不要）
- 将来のPostgreSQL移行を考慮し、SQLite固有機能への依存を最小化する

---

## 7. セキュリティ・プライバシー

- WISE v2はローカルアプリケーション（ネットワーク公開なし）
- APIはlocalhost限定で公開
- ファイルパスのPath Traversalはミドルウェアで検証
- 外部スクレイピングはRobot.txtおよびサイトToCを遵守する

---

## 8. 設計上の弱点・懸念点

### 8.1 EAVパターンの複雑なクエリ

METADATA_FIELDがEAV形式のため複合条件クエリが複雑になる。対策：FTS5でフルテキスト検索し、RDB側には主要フィールドのインデックスのみで対応。

### 8.2 SQLiteのJobポーリング競合

WALモード有効化でほぼ解決するが、高負荷時はポーリング間隔を2秒→5秒に緩和する設定を持つ。

### 8.3 IArchiveReaderの大ファイル対応

大容量アーカイブ（1GB超のCBZ等）でページストリーミングが遅くなる可能性がある。対策：先読みキャッシュ（前後2ページを事前ロード）を ComicViewer 側で実装する。

---

## 9. v2.0変更一覧（v1.1からの差分）

| 章節 | 変更内容 | 対応フィードバック |
|---|---|---|
| 3章 | MediaType抽象化を本格実装（Strategyパターン） | FB① |
| 4.1 | AssetRole導入（AssetTypeを廃止） | FB① |
| 4.2 | StorageFormat導入（ImageFolderを廃止） | FB③ |
| 4.3 | ICoverProvider Strategy Chain | FB④ |
| 4.4 | IMediaViewer DI（ViewerRoute + Capabilities） | FB⑧ |
| 4.5 | MediaDisplayProfile（Gallery if/else排除） | FB⑤ |
| 4.6 | IArchiveReader（解凍なしページストリーミング） | FB③④ |
| 4.7 | JobTypeにCoverExtract / ArchiveIndex / IndexUpdate追加 | FB④⑦ |
| 4.8 | FTS5全文検索（MediaType依存なし） | FB⑦ |
| 4.9 | Collection クロスメディア対応強化 | FB⑥ |
| 5章 | Plugin-Readyアーキテクチャ明文化 | FB⑨ |
| v1.1 6.5節「将来対応」 | → 本書3章で具体的に実装設計済み | - |

---

*WISE v2 Architecture.md v2.0 — 2026-06-30*
