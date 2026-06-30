# WISE v2 — Comic / Doujin Library Extension: Architecture Review & Roadmap

**Created:** 2026-06-30  
**Target Version:** WISE v2 (Post Sprint 13)  
**Status:** Architecture Design Document (Ready for Review)

---

## 0. TL;DR — 設計方針

> **Work エンティティに `MediaType` を追加するだけで、既存 Architecture の 95% を再利用できる。**

- ✅ EAV MetadataField → Comic フィールド (Circle, Author, Pages) を追加コスト 0 で吸収
- ✅ IMetadataProvider → `SupportedMediaTypes` を 1 プロパティ追加するだけで Comic Provider に対応
- ✅ Import Pipeline → ファイル拡張子検出に Comic フォーマットを追加するだけ
- ✅ Background Worker / Job Queue → 変更なし
- ✅ Gallery / Detail → UI コンポーネントだけ MediaType 分岐

---

## 1. 既存 Architecture レビュー

### 1-1. 強みと評価

| 設計 | 評価 | 理由 |
|---|---|---|
| EAV MetadataField | **最大の資産** | Comic 固有フィールドをスキーマ変更なしで追加可能 |
| Work 中心設計 | **そのまま使える** | Video/Comic どちらも Work を Aggregate Root に持つ |
| Evidence 積み上げ | **Comic に即活用** | RJ番号/ISBN を IEvidenceProvider として追加するだけ |
| Job Queue (DB) | **変更不要** | Comic Import/MetadataFetch も同じキューに投入 |
| Provider Pipeline | **Tier構造ごと再利用** | SupportedMediaTypes プロパティ 1 つで Comic 判別 |

### 1-2. 設計上の問題点と対策

| 問題 | 現状 | 対策 |
|---|---|---|
| Work に MediaType がない（暗黙的に Video を前提） | Scope を MVP に限定 | `MediaType` enum 追加、DEFAULT='Video' |
| AssetType に Video が実質存在しない | Unknown 扱い | AssetType 拡張（Video/Comic/Document 追加） |
| Import Pipeline がファイル種別を MediaType に変換していない | Video のみ想定 | 拡張子検出ロジック追加 |
| IMetadataProvider に SupportedMediaTypes がない | 全 Work に実行される | `IReadOnlySet<MediaType> SupportedMediaTypes` プロパティ追加 |
| FetchMetadata Pipeline が Video 固有処理をハードコード | FFmpeg, ファイル整理パス | MediaType 別分岐（メソッド抽出） |
| Detail ページが VideoPlayer を固定レンダリング | UI コンポーネント密結合 | `MediaViewer` に戦略パターン導入 |

---

## 2. MediaType 抽象化設計

### 2-1. 設計案比較

| 軸 | 案 A: Work に MediaType 列追加 | 案 B: Work 継承 (TPH/TPT) | 案 C: MediaTypeConfig テーブル |
|---|---|---|---|
| 保守性 | ◎ シンプル | △ EF Core 継承は複雑 | ○ 柔軟だが over-engineering |
| 拡張性 | ◎ MediaType enum 追加のみ | △ クラス追加 + Migration | ◎ ただしコード生成が必要 |
| テスト容易性 | ◎ | △ 継承テスト複雑 | ○ |
| パフォーマンス | ◎ 追加 JOIN なし | △ TPT で JOIN 増 | △ JOIN 増 |
| 実装難易度 | ◎ 低 | △ 高 | ○ 中 |

**→ Claude が選ぶのは 案A**  
Work の共通フィールド（Favorite, Rating, Tag, Identifier, Series）はすべての MediaType で意味を持つ。MediaType 固有の属性（Pages, BindingDirection）は EAV MetadataField として保存済み。

### 2-2. MediaType 定義

```csharp
public enum MediaType
{
    Video          = 1,   // 現在対応済み
    Comic          = 2,   // 今回追加
    Book           = 3,   // 将来 (ISBN)
    PhotoBook      = 4,   // 将来
    ImageCollection = 5,  // 将来
    Audio          = 6,   // 将来 (Music)
}
```

### 2-3. MediaTypeCapabilities 宣言型設計

```csharp
public record MediaTypeCapabilities(
    bool CanPlay,
    bool CanRead,
    bool CanView,
    bool HasPageCount,
    bool HasChapters,
    bool HasDuration,
    bool SupportsArchive
)
{
    public static readonly MediaTypeCapabilities Video = new(
        CanPlay: true, CanRead: false, CanView: false,
        HasPageCount: false, HasChapters: true, HasDuration: true, SupportsArchive: false);

    public static readonly MediaTypeCapabilities Comic = new(
        CanPlay: false, CanRead: true, CanView: false,
        HasPageCount: true, HasChapters: false, HasDuration: false, SupportsArchive: true);

    public static MediaTypeCapabilities For(MediaType t) => t switch
    {
        MediaType.Video => Video,
        MediaType.Comic => Comic,
        _ => new(false, false, false, false, false, false, false)
    };
}
```

---

## 3. Domain 変更点

### 3-1. Work エンティティ（追加のみ、破壊的変更なし）

```csharp
public class Work : Entity, IAggregateRoot
{
    public MediaType MediaType { get; private set; } = MediaType.Video;  // 追加
    public string? UserMemo { get; private set; }    // 追加（nullable）
    
    // 既存フィールドすべて保持
    public string? PrimaryIdentifier { get; }
    public ProcessingStatus Status { get; }
    public bool Favorite { get; }
    public int? Rating { get; }
    
    public MediaTypeCapabilities Capabilities => MediaTypeCapabilities.For(MediaType);  // 追加
}
```

### 3-2. Asset エンティティ — AssetType 拡張

```csharp
public enum AssetType
{
    Unknown        = 0,
    PortraitCover  = 1,
    LandscapeCover = 2,
    Thumbnail      = 3,
    SampleImage    = 4,
    Video          = 5,   // 追加
    Comic          = 6,   // 追加
    Document       = 7,   // 追加（PDF/EPUB）
    ImageFolder    = 8,   // 追加
}
```

### 3-3. IMetadataProvider 変更（1 プロパティのみ追加）

```csharp
public interface IMetadataProvider
{
    string ProviderId { get; }
    int Priority { get; }
    IReadOnlySet<MediaType> SupportedMediaTypes { get; }  // 追加（唯一の追加）
    Task<MetadataResult> FetchAsync(MetadataProviderContext context);
}
```

既存 Video Provider への対応: `new HashSet<MediaType>{MediaType.Video}` を追加するだけ。

### 3-4. 新 Domain Interface — IArchiveReader

```csharp
public interface IArchiveReader : IDisposable
{
    int PageCount { get; }
    IReadOnlyList<string> PageNames { get; }
    Task<Stream> GetPageStreamAsync(int pageIndex, CancellationToken ct);
    Task<Stream> GetCoverStreamAsync(CancellationToken ct);
}

public interface IArchiveReaderFactory
{
    bool CanRead(string filePath);
    IArchiveReader Create(string filePath);
}
```

### 3-5. ReaderState Value Object

```csharp
public record ReaderState(
    int CurrentPage,
    bool IsSpread,
    bool IsRightBinding,
    float ZoomLevel
)
{
    public static ReaderState Default => new(0, false, true, 1.0f);
}
```

---

## 4. DB 変更点

### 4-1. EF Core Migration（追加のみ、破壊的変更なし）

```sql
-- Migration: AddMediaTypeToWork

ALTER TABLE "Works" 
  ADD COLUMN "MediaType" INTEGER NOT NULL DEFAULT 1;
  -- (1 = Video, 既存レコードは全て Video)

ALTER TABLE "Works" 
  ADD COLUMN "UserMemo" TEXT NULL;

ALTER TABLE "Works" 
  ADD COLUMN "ReaderPosition" INTEGER NOT NULL DEFAULT 0;

-- 既存 Video Asset のAssetType更新
UPDATE "Assets" 
SET "AssetType" = 5  -- Video
WHERE "AssetType" = 0
  AND (lower("FilePath") LIKE '%.mp4'
    OR lower("FilePath") LIKE '%.mkv'
    OR lower("FilePath") LIKE '%.avi');
```

### 4-2. 既存テーブル構造への影響

| テーブル | 変更 | 破壊的変更 |
|---|---|---|
| Works | `MediaType`, `UserMemo`, `ReaderPosition` 列追加 | No (DEFAULT値あり) |
| Assets | `AssetType` enum 値追加 | No |
| MetadataFields | 変更なし（EAV で自動対応） | No |
| その他 | 変更なし | No |

### 4-3. 新インデックス

```sql
CREATE INDEX "IX_Works_MediaType" ON "Works" ("MediaType");
CREATE INDEX "IX_Assets_AssetType_WorkId" ON "Assets" ("AssetType", "WorkId");
```

---

## 5. API 変更点

### 5-1. 既存 API の後方互換拡張

```
GET /api/works
  + クエリパラメータ: mediaType=Video|Comic|Book（オプション）

GET /api/works/{id}
  + レスポンスに mediaType フィールド
  + capabilities フィールド（canPlay, canRead 等）
```

### 5-2. 新 Comic Reader API

```
# ページ一覧
GET /api/works/{id}/reader/pages
→ { pageCount: 120, pages: [...] }

# ページ画像ストリーミング
GET /api/works/{id}/reader/pages/{pageIndex}
→ Content-Type: image/jpeg
→ Cache-Control: public, max-age=3600
→ ETag: "{workId}-{pageIndex}"

# カバー画像
GET /api/works/{id}/reader/cover

# 読書位置保存
PATCH /api/works/{id}/reader/position
→ Body: { page: 42, isSpread: false, isRightBinding: true }

# アーカイブ解析
POST /api/works/{id}/reader/analyze
→ { pageCount: 120, detectedBinding: "right", coverUrl: "..." }
```

### 5-3. API バージョニング

現在 `/api/v1/` なし。破壊的変更が不要のため追加 API は `/api/works/{id}/reader/` 名前空間を使用。

---

## 6. UI 変更点

### 6-1. Gallery — MediaType フィルタ

Header に MediaType タブ追加: All / Video / Comic / Book

```tsx
const MEDIA_TABS = [
  { value: 'all', icon: <Library />, label: 'すべて' },
  { value: 'Video', icon: <Film />, label: '動画' },
  { value: 'Comic', icon: <BookOpen />, label: 'コミック' },
  { value: 'Book', icon: <Book />, label: '書籍' },
];
```

### 6-2. WorkCard — MediaType バッジ

Compact モードの右上に `VIDEO` `COMIC` `BOOK` バッジを表示。

### 6-3. DisplaySettings — Comic フィールド追加

```ts
export const DISPLAY_FIELD_LABELS = {
  // 既存
  identifier: '品番', title: 'タイトル', actress: '出演者',
  maker: 'メーカー', label: 'レーベル', releaseDate: '発売日',
  favorite: 'お気に入り', rating: '評価', status: 'ステータス',
  // 新規（Comic 対応）
  author: '作者', circle: 'サークル', pageCount: 'ページ数', language: '言語',
};
```

### 6-4. Detail ページ — MediaViewer 戦略パターン

```tsx
function MediaViewer({ work }: { work: WorkDetail }) {
  if (work.capabilities.canPlay) return <VideoPlayer ... />;
  if (work.capabilities.canRead) return <ComicReader workId={work.id} />;
  if (work.capabilities.canView) return <ImageViewer ... />;
  return null;
}
```

### 6-5. ComicReader コンポーネント（将来実装）

```
ComicReader
  ├── PageCanvas
  ├── SpreadCanvas
  ├── PageThumbnailStrip
  ├── ReaderControls
  │   ├── PrevButton / NextButton
  │   ├── PageInput
  │   ├── SpreadToggle
  │   ├── BindingToggle（右綴じ/左綴じ）
  │   └── ZoomControls
  └── KeyboardHandler
```

---

## 7. Repository 変更点

### 7-1. IWorkRepository

```csharp
// 既存メソッドで MediaType フィルタを対応
// 新規追加
Task<IReadOnlyList<Work>> GetByMediaTypeAsync(
    MediaType mediaType, 
    CancellationToken cancellationToken);
```

### 7-2. 新 IArchiveRepository

```csharp
public interface IArchiveRepository
{
    Task<int> GetPageCountAsync(Guid workId, CancellationToken ct);
    Task<Stream> GetPageStreamAsync(Guid workId, int pageIndex, CancellationToken ct);
    Task<ArchiveInfo> AnalyzeAsync(Guid workId, CancellationToken ct);
}
```

---

## 8. Metadata Provider 設計（Comic 向け）

### 8-1. 新 Provider 一覧

| Provider | 対象 Identifier | 優先度 | MediaType | フィールド |
|---|---|---|---|---|
| DlsiteMetadataProvider | RJ\d{6,8} | 80 | Comic, Video | Title/Circle/Author/Genre/Pages/ReleaseDate/Cover |
| FanzaDoujinProvider | d_\d{6,9} | 75 | Comic | Title/Circle/Author/Genre/Pages/Cover |
| MelonbooksProvider | RJ\d+/独自 | 50 | Comic, Book | Title/Circle/Author/Cover |
| BoothProvider | \d{7,} | 45 | Comic, Book | Title/Circle/Author/Cover |
| OpenBDProvider | ISBN | 60 | Book | Title/Author/Publisher/Pages/Cover |
| LocalComicNfoProvider | — | 40 | Comic, Book | ComicInfo.xml 解析 |

### 8-2. IEvidenceProvider 拡張

```csharp
public class RJNumberEvidenceProvider : IEvidenceProvider { ... }
public class ISBNEvidenceProvider : IEvidenceProvider { ... }
public class FanzaDoujinEvidenceProvider : IEvidenceProvider { ... }
```

---

## 9. Archive Reader 設計

### 9-1. 対応フォーマット

| フォーマット | NuGet | 実装クラス |
|---|---|---|
| .zip / .cbz | System.IO.Compression (標準) | ZipArchiveReader |
| .rar / .cbr | SharpCompress | RarArchiveReader |
| .pdf | Docnet.core (PdfiumViewer) | PdfArchiveReader |
| .epub | EpubCore | EpubArchiveReader |
| 画像フォルダ | なし | FolderArchiveReader |

### 9-2. ArchiveReaderFactory

```csharp
public class ArchiveReaderFactory : IArchiveReaderFactory
{
    public bool CanRead(string filePath) =>
        Readers.ContainsKey(Path.GetExtension(filePath).ToLower());

    public IArchiveReader Create(string filePath) =>
        Readers[Path.GetExtension(filePath).ToLower()](filePath);
}
```

### 9-3. キャッシュ戦略

| データ | キャッシュ先 | TTL | 理由 |
|---|---|---|---|
| ページ数 | Works テーブル | 永久 | 変わらない |
| ページ画像 | メモリキャッシュ (LRU 20ページ) | 10分 | 連続読みに対応 |
| カバーサムネイル | `.thumbnails/` | 永久 | 既存方式踏襲 |
| PDF ページレンダリング | `.page-cache/` | 永久 | 毎回レンダリングコスト大 |

---

## 10. Cover 抽出 Pipeline（優先順位）

```csharp
① Metadata Provider が取得したカバー URL
  ↓ (なければ)
② アーカイブ内 cover.jpg / cover.png
  ↓ (なければ)
③ Archive 内最初の画像
  ↓ (なければ)
④ PDF 1ページ目
  ↓ (なければ)
⑤ EPUB Cover
  ↓ (なければ)
⑥ フォルダ先頭画像
  ↓ (なければ)
⑦ サムネイル生成（縮小 or 最初ページ）
```

---

## 11. Import Pipeline 変更

### 11-1. ファイル検出 — MediaType 判定

```csharp
private static MediaType DetectMediaType(string filePath) => 
    Path.GetExtension(filePath).ToLower() switch
    {
        ".mp4" or ".mkv" or ".avi" => MediaType.Video,
        ".zip" or ".cbz" or ".rar" or ".cbr" => MediaType.Comic,
        ".pdf" => MediaType.Comic,
        ".epub" => MediaType.Book,
        _ => MediaType.Video   // DEFAULT
    };

private static AssetType DetectAssetType(string filePath) =>
    Path.GetExtension(filePath).ToLower() switch
    {
        ".mp4" or ".mkv" or ".avi" => AssetType.Video,
        ".zip" or ".cbz" or ".rar" or ".cbr" => AssetType.Comic,
        ".pdf" or ".epub" => AssetType.Document,
        _ => AssetType.Unknown
    };
```

### 11-2. 画像フォルダ検出

```csharp
private bool IsImageFolder(string dirPath)
{
    var files = Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly);
    if (files.Length < 3) return false;  // 最低3ページ
    var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
    return files.All(f => imageExtensions.Contains(Path.GetExtension(f)));
}
```

### 11-3. ファイル整理パス — MediaType 別

```
Video:  {LibraryRoot}/Video/{Maker or 女優名}/{Identifier}/
Comic:  {LibraryRoot}/Comic/{Circle}/{Identifier}/
Book:   {LibraryRoot}/Book/{Publisher}/{Identifier}/
```

---

## 12. Metadata Pipeline 変更

### 12-1. FetchMetadataJobUseCase — Provider フィルタ

```csharp
// 変更前: Video前提
var providers = _metadataProviders.OrderByDescending(p => p.Priority);

// 変更後: MediaType でフィルタ
var providers = _metadataProviders
    .Where(p => p.SupportedMediaTypes.Contains(work.MediaType))
    .OrderByDescending(p => p.Priority);
```

### 12-2. FFmpeg・ファイル整理の条件分岐

```csharp
// FFmpeg は Video のみ
if (work.MediaType == MediaType.Video)
    await _ffmpegService.GenerateThumbnailAsync(videoPath);
else if (work.MediaType == MediaType.Comic)
    await _comicCoverExtractor.ExtractCoverAsync(work, ct);

// ファイル整理パス
var targetDir = work.MediaType switch
{
    MediaType.Video => Path.Combine(libraryRoot, "Video", maker, identifier),
    MediaType.Comic => Path.Combine(libraryRoot, "Comic", circle, identifier),
    MediaType.Book => Path.Combine(libraryRoot, "Book", publisher, identifier),
    _ => Path.Combine(libraryRoot, identifier)
};
```

---

## 13. Sprint 単位ロードマップ

### Phase 2A: Foundation（2 Sprint）

#### Sprint 14: MediaType Foundation
**ゴール:** 既存コードを壊さずに MediaType を導入し、Comic ファイルを Import できる

| タスク | ファイル | 規模 |
|---|---|---|
| `MediaType` enum 追加 | Domain/Entities | XS |
| `Work.MediaType` プロパティ | Domain/Entities/Work.cs | XS |
| `AssetType` 拡張（Video/Comic/Document） | Domain/Entities/Asset.cs | XS |
| `IMetadataProvider.SupportedMediaTypes` | Domain/Interfaces | XS |
| 全 Video Provider に SupportedMediaTypes 実装 | Infrastructure/Providers/*.cs | S |
| EF Core Migration 作成・適用 | Infrastructure/Migrations | S |
| Import UseCase — MediaType/AssetType 検出 | Api/UseCases | M |
| FetchMetadata UseCase — Provider フィルタ | Api/UseCases | S |
| Gallery API — mediaType クエリパラメータ | Api/Controllers | S |

**品質ゲート:** 既存 Video Import が regression なく動作

#### Sprint 15: Archive Reader & Cover Extraction
**ゴール:** ZIP/CBZ を読み込み、カバー画像を自動抽出

| タスク | ファイル | 規模 |
|---|---|---|
| `IArchiveReader` / `IArchiveReaderFactory` | Domain/Interfaces | S |
| `ZipArchiveReader` 実装 | Infrastructure/Archive | M |
| `FolderArchiveReader` 実装 | Infrastructure/Archive | S |
| `ComicCoverExtractor` 実装 | Application/Services | M |
| `GET /api/works/{id}/reader/pages` | Api/Controllers | S |
| `GET /api/works/{id}/reader/pages/{n}` | Api/Controllers | M |
| ETag / Cache-Control 設定 | Api/Controllers | S |
| `POST /api/works/{id}/reader/analyze` | Api/Controllers | S |
| NuGet: SharpCompress 追加 | WISE.Infrastructure.csproj | XS |

**品質ゲート:** ZIP の全ページをブラウザで表示可能

---

### Phase 2B: Metadata & Identifier（2 Sprint）

#### Sprint 16: Comic Identifier & Evidence Providers

| タスク | 規模 |
|---|---|
| `RJNumberEvidenceProvider` | S |
| `ISBNEvidenceProvider` | S |
| `FanzaDoujinEvidenceProvider` | S |
| IdentifierResolver Context に MediaType 追加 | S |
| Identifier 正規化（RJ123456） | S |
| テスト追加（IdentifierParserTests） | M |

#### Sprint 17: DLsite & FANZA 同人 Provider

| タスク | 規模 |
|---|---|
| `DlsiteMetadataProvider` 実装 | L |
| `FanzaDoujinMetadataProvider` 実装 | L |
| `LocalComicNfoProvider`（ComicInfo.xml） | M |
| Comic MetadataField 定義確立 | S |
| Provider Diagnostic 拡張 | S |

---

### Phase 2C: UI（2 Sprint）

#### Sprint 18: Gallery + Detail（Comic 対応）

| タスク | 規模 |
|---|---|
| `useGalleryStore` — mediaTypeFilter 追加 | S |
| Header — MediaType タブ | S |
| WorkCard — MediaType バッジ | S |
| DisplaySettings — Comic フィールド | S |
| `MediaViewer` コンポーネント | M |
| Detail — capabilities 分岐 | M |
| Detail — Comic 向け InfoBlock | S |

#### Sprint 19: Comic Reader UI（将来）

| タスク | 規模 |
|---|---|
| `ComicReader` コンポーネント | L |
| PageCanvas + SpreadCanvas | M |
| PageThumbnailStrip | M |
| ReaderControls | M |
| キーボードショートカット | S |
| 読書位置リストア | S |

---

### Phase 2D: Advanced（2 Sprint）

#### Sprint 20: PDF / EPUB 対応

| タスク | 規模 |
|---|---|
| `PdfArchiveReader` 実装 | L |
| PDF ページキャッシュ | M |
| `EpubArchiveReader` 実装 | L |
| EPUB カバー抽出 | S |

#### Sprint 21: Book / OpenBD Provider

| タスク | 規模 |
|---|---|
| MediaType.Book 対応（ISBN） | M |
| `OpenBDProvider` 実装 | L |
| `MelonbooksProvider` 実装 | L |

---

### Phase 2E: Quality（1 Sprint）

#### Sprint 22: Integration & Performance

| タスク | 規模 |
|---|---|
| E2E テスト | L |
| Archive Reader パフォーマンス測定 | M |
| メモリキャッシュチューニング | S |
| Document 更新 | S |

---

## 14. リスク

### リスク A — RAR/CBR ライブラリ（中度）

**問題:** SharpCompress は RAR5 非対応。古い CBR は RAR4 が多いが RAR5 も存在。

**対策:** RAR5 は初期スコープ外。フォールバック対応。必要に応じて WinRAR CLI 呼び出し検討。

### リスク B — PDF レンダリング品質（中度）

**問題:** Docnet.core（PDFium バインディング）はネイティブバイナリ依存。Windows x64 では動作するが macOS/Linux 対応時に要注意。

**対策:** 初期は Windows only を明示。将来的な cross-platform 対応時にライブラリ評価し直す。

### リスク C — DLsite / FANZA 同人 スクレイピング（高度）

**問題:** 年齢認証。規約変更でスクレイピングが阻害されるリスク。

**対策:** 既存 FANZA Provider の Cookie 管理パターンをそのまま流用。API 優先。

### リスク D — 既存 Migration との整合性（低度）

**問題:** EF Core Migration は追加のみ。DEFAULT 値でバックフィルするため データ損失なし。

**対策:** 既存 wise.db をバックアップしてから Migration を適用。

### リスク E — ComicReader パフォーマンス（中度）

**問題:** 高解像度スキャンは 1 ページ 10MB 超もある。

**対策:** API 側で WebP 変換・リサイズ（ImageSharp）。最大 2000px に制限。

---

## 15. 優先順位

```
優先度 1 (Core): Sprint 14-15
  → MediaType Foundation + Archive Reader
  → これがなければ後続は全て不可

優先度 2 (Value): Sprint 16-17
  → Identifier + Metadata Provider
  → Metadata がなければ Gallery が空

優先度 3 (UX): Sprint 18-19
  → UI + ComicReader
  → ユーザーが実際に使える形

優先度 4 (Completeness): Sprint 20-21
  → PDF/EPUB + Book
  → フォーマット対応拡充

優先度 5 (Quality): Sprint 22
  → テスト + チューニング
```

---

## 16. 既存 Document との差分

| Document | 変更要否 | 内容 |
|---|---|---|
| Architecture.md | **要更新** | MediaType 抽象化の追加、IArchiveReader の追加 |
| Domain.md | **要更新** | Work.MediaType, AssetType 拡張, IArchiveReader |
| Database.md | **要更新** | Works.MediaType カラム、新インデックス |
| Metadata.md | **要更新** | SupportedMediaTypes, Comic フィールド定義 |
| Pipeline.md | **要更新** | Cover Extraction Pipeline, Comic Import フロー |
| API.md | **要更新** | `/reader/` 新エンドポイント |
| Roadmap.md | **要更新** | Phase 2 として Comic/Doujin 追加 |
| Identifier.md | **要更新** | RJNumber/ISBN EvidenceProvider 追加 |

**既存 Document と矛盾する点なし。すべて「追加」として記述可能。**

---

## 17. より良い設計案（追加提案）

### 17-1. UserTag / Genre の正規化テーブル化

現在 Genre は `MetadataField.FieldName = "Genre"` として `|` 区切りで保存。これはクロス検索を困難にしている。

**提案:** `Tag` テーブルを導入し、Work と N:M 関係にする（Collection 機能と同時実装を推奨）。

```sql
CREATE TABLE "Tags" (
    "Id" TEXT PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Category" TEXT NOT NULL,
    "MediaType" TEXT NULL
);

CREATE TABLE "WorkTags" (
    "WorkId" TEXT NOT NULL,
    "TagId" TEXT NOT NULL,
    PRIMARY KEY ("WorkId", "TagId")
);
```

### 17-2. Search 改善 — FTS5 導入

現在は `LIKE '%keyword%'` による全件スキャン。10,000件以上では遅い。

**提案:** SQLite FTS5（全文検索）を有効化。

```sql
CREATE VIRTUAL TABLE "WorkSearchIndex" USING fts5(
    work_id UNINDEXED,
    identifier,
    title,
    actress,
    circle,
    author,
    maker,
    genres
);
```

### 17-3. Work.Capabilities は Static Method に

```csharp
// プロパティではなく Domain Service として分離
public static class MediaCapabilityService
{
    public static MediaTypeCapabilities GetCapabilities(MediaType mediaType) => ...;
}
```

理由: Work エンティティが Capabilities ロジックを持つと単一責任原則に反する可能性がある。

---

## 18. 将来 MediaType 追加時の設計指針

新しい MediaType を追加するコストは以下だけになる：

```
新 MediaType "Audio" を追加する場合:

1. MediaType enum に Audio = 6 を追加（1行）
2. MediaTypeCapabilities.For に case 追加（3行）
3. 新 AssetType.Audio 追加（1行）
4. Audio 用 IEvidenceProvider 実装（MusicBrainz, ID3 tag）
5. Audio 用 IMetadataProvider 実装（MusicBrainz API）
6. Import Pipeline に .mp3/.flac/.m4a 検出追加（3行）
7. MediaViewer に AudioPlayer コンポーネント追加（UI）
8. EF Core Migration: 不要（MediaType=6 は自動対応）
```

**Domain / Infrastructure / Repository への変更はゼロ。**

---

## 19. Claude の最終推奨

### 採用設計

案A（Work に MediaType 列追加）+ IMetadataProvider.SupportedMediaTypes + IArchiveReader 導入

### 理由

1. **既存設計への敬意** — EAV MetadataField は Comic のために作られたかのように完璧に機能する。9年後も同じ設計が通用する。

2. **差分最小** — Domain を壊さず、Infrastructure を拡張し、UI だけが MediaType を意識する。SOLID の Open/Closed 原則の教科書通り。

3. **Product Constitution の維持** — 「Metadata First」「引き算の美学」「Human In The Loop」はすべて既存構造で維持される。Comic だからといって特別扱いしない。

4. **漸進的な実装** — Sprint 14-15 だけで Comic を Import してアーカイブを開ける。Sprint 16-17 で Metadata が充実。リスクを小さく保てる。

5. **Plugin Architecture への準備** — IArchiveReader / IMetadataProvider に SupportedMediaTypes を持たせることで、将来の外部 Plugin SDK でサードパーティが独自 MediaType を追加できる基盤になる。

### 唯一の懸念

DLsite/FANZA 同人のスクレイピング安定性は技術的問題ではなくプロバイダー側の制約。Cookie 管理 + Fallback 戦略（LocalComicNfoProvider）で最大限に緩和する。

---

## 附録: 実装チェックリスト

### Phase 2A

- [ ] MediaType enum 定義
- [ ] Work.MediaType DEFAULT='Video'
- [ ] Work.UserMemo フィールド
- [ ] AssetType 拡張
- [ ] IMetadataProvider.SupportedMediaTypes インターフェース
- [ ] 全 Video Provider 更新
- [ ] EF Core Migration
- [ ] Import メディア型判定
- [ ] FetchMetadata Provider フィルタ
- [ ] Gallery API — mediaType パラメータ
- [ ] IArchiveReader インターフェース
- [ ] IArchiveReaderFactory インターフェース
- [ ] ZipArchiveReader 実装
- [ ] ComicCoverExtractor
- [ ] `/api/works/{id}/reader/pages` エンドポイント
- [ ] `/api/works/{id}/reader/pages/{n}` エンドポイント
- [ ] SharpCompress NuGet 追加

### Phase 2B

- [ ] RJNumberEvidenceProvider
- [ ] ISBNEvidenceProvider
- [ ] FanzaDoujinEvidenceProvider
- [ ] DlsiteMetadataProvider
- [ ] FanzaDoujinMetadataProvider
- [ ] LocalComicNfoProvider
- [ ] Comic MetadataField 定義

### Phase 2C

- [ ] useGalleryStore — mediaTypeFilter
- [ ] Header — MediaType タブ
- [ ] WorkCard — MediaType バッジ
- [ ] DisplaySettings — Comic フィールド
- [ ] MediaViewer コンポーネント
- [ ] Detail capabilities 分岐

---

*設計レビュー作成日: 2026-06-30*  
*対象バージョン: WISE v2 (Sprint 13 完了時点)*  
*ステータス: Architecture Design Ready for Implementation*
