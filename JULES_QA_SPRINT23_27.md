# WISE v2 — Sprint 23〜27 QA プロンプト
# Jules QA Engineer 向け（コード修正禁止）

あなたは Release QA Engineer です。
コードを修正してはいけません。リファクタリングも禁止。
修正案はコメントに書いても構いませんが、**コード変更は一切禁止**です。

**PASSを探すのではありません。FAILを探してください。**
特に「動作するが想定外の副作用がある」「境界条件でだけ壊れる」ケースを重視してください。

---

## 対象スプリントの実装概要

### Sprint 23 — 安定性
- `EnumerateSafe`: BFS ストリーミングで `UnauthorizedAccessException` / `IOException` をスキップ
- Polly: 全 HTTP Provider に exponential backoff retry（3回、2^n 秒）
- `MetadataService.GetTier1ExitFields(MediaType)`: Comic → Title/Author/Circle、Video → Title/Actress/Maker
- `IMetadataProvider.SupportedMediaTypes`: 各 Provider が対応 MediaType を宣言、MetadataService が事前フィルタ

### Sprint 24 — DB パフォーマンス
- Migration: Works(Status, MediaType, Favorite) + MetadataFields(FieldName, Value) インデックス追加
- Cover API (`GET /api/works/{id}/cover`): ETag + `Cache-Control: public, max-age=86400` + 304 応答
- FTS5 RebuildFts: FetchMetadata 完了後に自動キューイング、BackgroundJobWorker が `INSERT INTO METADATA_FIELD_FTS(METADATA_FIELD_FTS) VALUES('rebuild')` を実行

### Sprint 25 — Detail Page UX
- `WorkCard`: Normal/Compact はホバー前カバーのみ（hover overlay）。Rich のみ info strip 常時表示
- `GalleryGrid.INFO_HEIGHT["normal"] = 0`（仮想スクロール行高の修正）
- Detail Page: ファイル（Assets）セクションをアコーディオン化（デフォルト閉じ）

### Sprint 26 — Scraping 強化
- `RateLimiterService` (Singleton): ドメイン別 Token Bucket。`appsettings.json` の `RateLimiter` セクションで設定
- `CachingHandler` (DelegatingHandler): GET レスポンスを SQLite `HttpCaches` テーブルに TTL=24h でキャッシュ
- `RateLimitingHandler` (DelegatingHandler): リクエスト前にトークン取得
- Pipeline 順序: `CachingHandler` → `RateLimitingHandler` → `Polly retry` → network

### Sprint 27 — JavLibrary / DB 整合性 / UX
- `JavLibraryMetadataProvider`: Video 向け (Priority=45)。javlibrary.com/ja/ を検索 → パース（デフォルト無効）
- `SqlitePragmaInterceptor`: `DbConnectionInterceptor` で接続ごとに `PRAGMA foreign_keys = ON`
- `DEFAULT_DISPLAY.identifier = false`: リスト表示の品番列がデフォルト非表示
- `WorkListRow` / `GalleryGrid`: `displayFields.identifier` を参照して品番列を条件表示

---

## Step 1: 環境準備

### 1-1. プロジェクトのビルド確認

```powershell
cd src/WISE.Api
dotnet build
```

エラーが出たら即 FAIL 報告。警告は列挙するだけで OK。

### 1-2. Migration 適用確認

```powershell
dotnet ef database update --project ../WISE.Infrastructure --startup-project .
```

`Sprint24_PerformanceIndexes` と `Sprint26_HttpCache` の両マイグレーションが適用されることを確認。

```sql
-- インデックスが実際に作成されているか確認
SELECT name FROM sqlite_master WHERE type='index' ORDER BY name;
```

期待値に含まれるべきインデックス:
- `IX_Works_Status`
- `IX_Works_MediaType`
- `IX_Works_Favorite`
- `IX_MetadataFields_FieldName_Value`
- `IX_HttpCaches_Url`
- `IX_HttpCaches_ExpiresAt`

### 1-3. PRAGMA foreign_keys 確認

```sql
PRAGMA foreign_keys;
-- → 1 が返ること（0 なら SqlitePragmaInterceptor が機能していない）
```

**注意**: SQLite CLI から直接接続した場合は 0 になる（EF Core 経由でのみ有効になる）。
確認方法: アプリ起動後に FK 違反を起こすデータを INSERT して拒否されるか確認。

```sql
-- FK 違反テスト: 存在しない WorkId を参照するアセットを INSERT
INSERT INTO Assets (Id, WorkId, FilePath, FileSize, OriginalFilename, AssetType, CreatedAt, UpdatedAt)
VALUES ('00000000-0000-0000-0000-000000000001',
        '99999999-9999-9999-9999-999999999999',
        'C:/fake/path.mp4', 0, 'fake.mp4', 1, datetime('now'), datetime('now'));
-- → FOREIGN KEY constraint failed エラーが出ること
```

---

## Step 2: Sprint 23 — EnumerateSafe テスト

### 2-1. アクセス制限ディレクトリ混在テスト

```powershell
# テスト用フォルダ構造を作成
$base = "C:\WiseQA\ImportTest_Auth"
New-Item -ItemType Directory -Force -Path "$base\normal\subfolder"
New-Item -ItemType Directory -Force -Path "$base\restricted"
New-Item -ItemType File -Force -Path "$base\normal\SONE-001.mp4"
New-Item -ItemType File -Force -Path "$base\normal\subfolder\PRED-999.mp4"
New-Item -ItemType File -Force -Path "$base\restricted\ABC-123.mp4"

# restricted フォルダへのアクセスを拒否
icacls "$base\restricted" /deny "Everyone:(OI)(CI)R"
```

```http
POST /api/jobs/import
{
  "folderPath": "C:\\WiseQA\\ImportTest_Auth",
  "mode": "Copy"
}
```

**期待動作**: ジョブが `Completed` になり、`normal` フォルダの SONE-001.mp4 と PRED-999.mp4 が Import される。`restricted` フォルダのファイルはスキップ（エラーではなく partial success）。

**FAIL 条件**:
- ジョブが `Failed` になる
- Import 結果が 0 件になる
- ログに `UnauthorizedAccessException` が `Error` レベルで出る（`Warning` または `Information` ならセーフ）

### 2-2. SupportedMediaTypes フィルタ確認

Comic Work（`.zip` / `.cbz`）に対して FetchMetadata を実行し、プロバイダーのログを確認。

```sql
-- テスト用 Comic Work を確認
SELECT Id, PrimaryIdentifier, MediaType FROM Works WHERE MediaType = 2 LIMIT 5;
```

```http
POST /api/jobs/fetchmetadata
{ "workId": "<Comic Work の ID>" }
```

ジョブ完了後にログ確認。

**期待動作**: `FanzaMetadataProvider`, `MgsMetadataProvider`, `JavBusMetadataProvider`, `AvWikiMetadataProvider` のログが**一切出ない**。
`DLSiteMetadataProvider`, `ComicInfoXmlMetadataProvider`, `DoujinishiFilenameMetadataProvider` のログが出る。

**FAIL 条件**:
- `[Fanza]` のログが Comic に対して出ている
- `[JavBus]` が Comic に対して HTTP リクエストを送っている

### 2-3. Tier1ExitFields 早期終了確認

Comic Work で Title + Author + Circle が揃った場合に、それ以降の Provider が呼ばれないことを確認。

```sql
-- Title / Author / Circle が揃っている Comic Work を探す
SELECT DISTINCT w.Id, w.PrimaryIdentifier
FROM Works w
JOIN MetadataFields mf ON mf.WorkId = w.Id
WHERE w.MediaType = 2
  AND w.Id IN (SELECT WorkId FROM MetadataFields WHERE FieldName = 'Title')
  AND w.Id IN (SELECT WorkId FROM MetadataFields WHERE FieldName = 'Author')
  AND w.Id IN (SELECT WorkId FROM MetadataFields WHERE FieldName = 'Circle')
LIMIT 3;
```

その Work に対して FetchMetadata を実行してログ確認。DLSite が Title/Author/Circle を返したら Tier1 脱出しているはず。

**FAIL 条件**:
- Tier1 フィールドが揃っているのに低優先度 Provider（Getchu, AvWiki 等）が発火する
- ログに "tier1 exit" 相当のメッセージが出ず、全 Provider が走る

---

## Step 3: Sprint 24 — ETag / FTS5 テスト

### 3-1. Cover ETag / 304 テスト

```http
# 1回目: ETag を受け取る
GET /api/works/{id}/cover
→ レスポンスヘッダーに ETag: "xxxxxxxx" と Cache-Control: public, max-age=86400 があること

# 2回目: If-None-Match を送る
GET /api/works/{id}/cover
If-None-Match: "xxxxxxxx"
→ 304 Not Modified が返り、ボディが空であること
```

**FAIL 条件**:
- ETag ヘッダーが存在しない
- 304 が返らず 200 が返る
- 304 のレスポンスにボディが含まれる（ブラウザがキャッシュできなくなる）
- ETag の値が `\"` で囲まれていない（`W/` prefix なし + ダブルクォートで囲まれていること）

### 3-2. FTS5 全文検索テスト

FetchMetadata 完了後に RebuildFts ジョブが自動追加されているか確認。

```sql
SELECT JobType, Status, CreatedAt FROM Jobs
WHERE JobType = 'RebuildFts'
ORDER BY CreatedAt DESC
LIMIT 5;
```

RebuildFts が Completed になった後に全文検索が動作するか確認。

```http
GET /api/works?search=タイトルの一部
→ 結果が返ること
```

**FAIL 条件**:
- RebuildFts ジョブが生成されない
- RebuildFts が Failed になる（ログ確認）
- FTS5 rebuild 後も検索が 0 件を返す（インデックスが壊れている）
- RebuildFts が毎回多重追加される（同一実行中に複数キューされる）

### 3-3. インデックス EXPLAIN 確認

```sql
EXPLAIN QUERY PLAN
SELECT * FROM MetadataFields WHERE FieldName = 'Title' AND Value LIKE 'EKDV%';
-- → "USING INDEX IX_MetadataFields_FieldName_Value" が出ること（SCAN TABLE は FAIL）

EXPLAIN QUERY PLAN
SELECT * FROM Works WHERE Status = 'Completed';
-- → "USING INDEX IX_Works_Status" が出ること
```

---

## Step 4: Sprint 25 — WorkCard / Detail Page UX テスト

### 4-1. WorkCard 行高が崩れていないか（Normal density）

フロントエンドを起動し、Normal density でギャラリーを表示。

```
http://localhost:3000
```

確認項目:
- [ ] Normal density でカードが重なっていない / 余白が異常でない
- [ ] ホバー前はカバー画像のみ表示（タイトル / 品番が見えない）
- [ ] ホバー時に下部グラデーション + タイトル / 出演者が表示される
- [ ] Compact density でも同様の挙動
- [ ] Rich density では info strip が常時表示される

**不安なポイント（実装者コメント）**:
`INFO_HEIGHT["normal"]` を 88→0 に変更したが、仮想スクロールの行高推定と実際のカード高さが一致しているか不確か。スクロール後にカードが重なる・ずれる現象が出る可能性がある。

**FAIL 条件**:
- スクロール後にカードが重なる / 前の行が透けて見える
- Normal density でカード下部に 88px 相当の余白が残っている
- ホバー後もオーバーレイが表示されない

### 4-2. Detail Page — Assets アコーディオン

```
http://localhost:3000/works/{id}
```

アセットが複数ある Work を開く。

- [ ] ページ初期表示時に「ファイル (N)」のアコーディオンが**閉じている**
- [ ] クリックで開き、ファイル一覧が表示される
- [ ] もう一度クリックで閉じる
- [ ] ファイルが 1 件の場合も正しく動作する
- [ ] アセットが 0 件の Work では「ファイル」セクション自体が非表示

**FAIL 条件**:
- 初期表示でアコーディオンが開いている（デフォルト `false` が効いていない）
- ファイル数 (N) が実際の件数と違う
- 開閉でページのスクロール位置がリセットされる

### 4-3. zustand persist との互換性

以前から WISE を使っていたユーザー（`wise-gallery-v2` の LocalStorage キーが存在）は、persist された `displayFields` を持っている。

```javascript
// ブラウザの DevTools Console で確認
JSON.parse(localStorage.getItem('wise-gallery-v2'))?.state?.displayFields
// → identifier が true / false どちらになっているか確認
```

**設計上の質問**: 既存ユーザーが persist state を持っている場合、`identifier: false` のデフォルト変更は**効かない**（persist が優先される）。これは意図的な設計か、それとも migration ロジックが必要か確認してほしい。

---

## Step 5: Sprint 26 — レートリミット / キャッシュ テスト

### 5-1. HTTP キャッシュ — 基本動作

`IsEnabled: true` になっているプロバイダー（DLSite 等）に対して同じ Work の FetchMetadata を 2 回実行。

```http
POST /api/jobs/fetchmetadata
{ "workId": "<DLSite で取得できる Work の ID>" }
```

1回目完了後、すぐに2回目を実行。

```sql
-- HttpCaches テーブルに行が追加されているか
SELECT Url, CachedAt, ExpiresAt, length(Body) as BodySize
FROM HttpCaches
ORDER BY CachedAt DESC
LIMIT 10;
```

**期待動作**:
- 1回目: `[HttpCache] STORE {Url}` ログが出る
- 2回目: `[HttpCache] HIT {Url}` ログが出る（HTTP リクエストが飛ばない）
- 2回目の実行時間が 1回目より大幅に短い

**FAIL 条件**:
- `HttpCaches` テーブルが空のまま
- 2回目も HIT にならず STORE される（URL が変わっている可能性: クエリパラメータ、リダイレクト後 URL との不一致）
- 2回目でも実 HTTP リクエストが飛んでいる（ログで確認）

### 5-2. HTTP キャッシュ — TTL 期限切れ確認

```sql
-- 期限切れキャッシュエントリを手動で作成
UPDATE HttpCaches SET ExpiresAt = datetime('now', '-1 hour')
WHERE Id = (SELECT Id FROM HttpCaches LIMIT 1);
```

同じ URL で再取得をトリガーして、期限切れキャッシュが使われないことを確認。

**FAIL 条件**: 期限切れキャッシュを返してしまう（`ExpiresAt > DateTime.UtcNow` チェックが機能していない）

### 5-3. HTTP キャッシュ — 並行リクエスト競合

同一 Work に対して FetchMetadata ジョブを**同時に 2 件**キューする。

```http
POST /api/jobs/fetchmetadata { "workId": "SAME_ID" }
POST /api/jobs/fetchmetadata { "workId": "SAME_ID" }
```

**FAIL 条件**:
- `UNIQUE constraint failed: HttpCaches.Url` エラーがログに出る
- アプリがクラッシュ / 500 になる
- EF Core の `DbUpdateException` がスローされる

**実装者コメント**: CachingHandler 内で同一 URL の並行 INSERT が起きた場合、`UNIQUE` 制約違反が発生する可能性がある。現在は `try/catch` で吸収しているが、実際にどう振る舞うか不確か。

### 5-4. レートリミット — トークン枯渇時の待機

連続で FetchMetadata ジョブを 10 件キューし、ログでレートリミット待機が出ることを確認。

```
[RateLimit] {domain}: waiting {N}s for token
```

**FAIL 条件**:
- ログが一切出ない（RateLimitingHandler が呼ばれていない）
- `OperationCanceledException` でジョブが失敗する（待機中にキャンセルされている）
- 10 件が瞬時に完了する（レートリミットが効いていない）

### 5-5. キャッシュ → レートリミット パイプライン順序確認

キャッシュ HIT 時はレートリミットのログが**出ない**ことを確認。
（`CachingHandler` が最外 → キャッシュ HIT なら `RateLimitingHandler` は実行されない設計）

```
1回目: [HttpCache] STORE + [RateLimit] waiting が出る ← 両方出る
2回目: [HttpCache] HIT のみ ← RateLimit ログが出ない
```

**FAIL 条件**: キャッシュ HIT 時に `[RateLimit]` ログが出る（ハンドラー順序が逆になっている）

### 5-6. キャッシュ Body サイズ確認

大きなページ（FANZA の Product ページは 200KB+ になることがある）でも保存・読み込みが正常か確認。

```sql
SELECT Url, length(Body) as BodyBytes FROM HttpCaches ORDER BY BodyBytes DESC LIMIT 5;
```

**FAIL 条件**:
- `BodyBytes` が 0 または極端に小さい（Content が消費後に空になっている）
- `BodyBytes` が異常に大きい（1MB 超）が蓄積してDBが膨張している

**実装者コメント**: `CachingHandler` は `await response.Content.ReadAsStringAsync()` でボディを消費した後、`response.Content` を新しい `StringContent` で置き換えている。Provider が Content を再度読もうとした場合に空になっていないか確認が必要。

---

## Step 6: Sprint 27 — JavLibrary / FK / 品番列 テスト

### 6-1. JavLibraryMetadataProvider — 有効化テスト

`appsettings.json` を一時的に変更して有効化。

```json
"JavLibrary": { "Priority": 45, "IsEnabled": true }
```

既知の AV タイトルの Work に対して FetchMetadata を実行。

**期待動作**:
- `[JavLibrary] Search=https://www.javlibrary.com/ja/vl_searchbyid.php?keyword=SONE-001` のログが出る
- Cloudflare に遮られた場合: `[JavLibrary] Cloudflare protection detected` が出てジョブが graceful fail
- 成功した場合: `[JavLibrary] Title=...` `[JavLibrary] Actress=...` ログが出る

**FAIL 条件**:
- Cloudflare 非検出で HTML パースに失敗し `NullReferenceException`
- `HttpRequestException` で `OperationCanceledException` が伝播してジョブが `Canceled` になる
- `[JavLibrary]` が Comic Work に対して発火する（`SupportedMediaTypes` が効いていない）

### 6-2. JavLibrary — 無効時のスキップ確認

`IsEnabled: false`（デフォルト）のとき、FetchMetadata で JavLibrary のログが**一切出ない**ことを確認。

**FAIL 条件**: `IsEnabled: false` でも HTTP リクエストが飛んでいる

### 6-3. PRAGMA foreign_keys — 実際の FK 違反テスト

アプリ起動中に EF Core 経由で FK 違反データを INSERT して拒否されることを確認。

方法: Swagger UI または直接 `dbContext.Database.ExecuteSqlRaw()` で試みる。
または、存在しない WorkId を持つ Asset を API 経由で作成しようとする。

**FAIL 条件**: FK 違反が通って `Assets` テーブルに孤立行が作れてしまう

```sql
-- 事後確認: 孤立 Asset が存在しないか
SELECT count(*) FROM Assets WHERE WorkId NOT IN (SELECT Id FROM Works);
-- → 0 であること
```

### 6-4. リスト表示 — 品番列デフォルト非表示

```
http://localhost:3000
```

ギャラリーを List density に変更し、品番列の表示を確認。

- [ ] 初回訪問（LocalStorage 未設定）では品番列が非表示
- [ ] Display Settings で「品番」を ON にすると品番列が表示される
- [ ] 品番列が非表示のとき、タイトル列が正しく左詰めになる（レイアウト崩れがない）
- [ ] ヘッダーの品番ソートボタンも非表示になる（WorkListRow と GalleryGrid が同期している）

**FAIL 条件**:
- 品番列が非表示でも幅だけ確保されて余白になる
- ヘッダーの品番ボタンと行の品番列の表示/非表示が不一致
- 品番 OFF のとき、品番ソートを選択済みの場合にソートが不定になる

---

## Step 7: 横断的なテスト

### 7-1. Provider × MediaType のマトリクス確認

| Provider | Video | Comic | Book |
|---|---|---|---|
| FanzaMetadataProvider | ✅ 発火 | ❌ 非発火 | ❌ 非発火 |
| DLSiteMetadataProvider | ✅ 発火 | ✅ 発火 | ✅ 発火 |
| MgsMetadataProvider | ✅ 発火 | ❌ 非発火 | ❌ 非発火 |
| ComicInfoXmlMetadataProvider | ❌ 非発火 | ✅ 発火 | ✅ 発火 |
| DoujinishiFilenameMetadataProvider | ❌ 非発火 | ✅ 発火 | ✅ 発火 |
| JavLibraryMetadataProvider (有効時) | ✅ 発火 | ❌ 非発火 | ❌ 非発火 |

各セルを実際のログで確認してFAIL報告。

### 7-2. API 応答の健全性確認

```http
GET /api/works?limit=9999
→ 200 かつ JSON パースできること。OOM で 500 にならないこと。

GET /api/works/{Video Work ID}
→ coverUrl, title, actress, mediaType フィールドが存在すること

GET /api/works/{Comic Work ID}
→ author, circle フィールドが存在すること（actress ではない）

GET /api/works/{存在しない ID}
→ 500 ではなく 404

GET /api/works/{id}/cover
→ ETag ヘッダーが存在すること
→ Cache-Control: public, max-age=86400 が存在すること
```

### 7-3. HttpCache テーブルの肥大化確認

大量の FetchMetadata を実行した後。

```sql
SELECT count(*), sum(length(Body)) / 1024 / 1024 as TotalMB FROM HttpCaches;
```

**懸念**: TTL が 24h なので、長期間使用すると `HttpCaches` がサイズ無制限に膨張する。TTL 期限切れエントリの自動削除（VACUUM / 定期 DELETE）は**未実装**。これは既知の課題として報告。

### 7-4. 仮想スクロールの行高テスト

ギャラリー Normal density で 500 件以上の作品を表示し、高速スクロールする。

**FAIL 条件**:
- カードが重なる
- 前の行の残像が出る
- スクロール後にカードが正しい位置に表示されない
- INFO_HEIGHT=0 なのにカード間に 88px の余白が残る

---

## Step 8: 実装者が特に不安なポイント

以下は実装者自身が「正しく動くか確信が持てない」と考えている箇所です。優先的に確認してください。

### ⚠️ 高優先

1. **CachingHandler の Body 二重消費**
   - `ReadAsStringAsync()` で消費後に `response.Content = new StringContent(body)` で置き換えているが、Provider がその後 Content にアクセスした場合に空になる可能性。
   - 確認: Provider がレスポンス Body を取得できているか（Title フィールドが入っているか）。

2. **CachingHandler の並行 INSERT 競合**
   - 同一 URL に同時リクエストが来た場合、`UNIQUE` 制約違反で EF Core が例外を投げる可能性。
   - 現在 `try/catch` で握りつぶしているが、2回目以降も Body が正しく返るか確認。

3. **RateLimiterService の長時間待機中キャンセル**
   - `await Task.Delay(delay, ct)` 中に CancellationToken がキャンセルされた場合、`OperationCanceledException` が上位に伝播する。
   - Polly retry が `OperationCanceledException` を retry 対象にしている場合、無限ループが起きる可能性。確認: `Policy.Handle<HttpRequestException>().OrResult(r => ...)` は OperationCanceledException を retryしない設計のはず。

4. **SqlitePragmaInterceptor が Singleton DbContext 再利用で呼ばれない**
   - EF Core の connection pooling によっては `ConnectionOpened` が初回のみ呼ばれ、再利用時に呼ばれない可能性。
   - 確認方法: API を大量に呼び出した後に FK 違反テストを再実行。

5. **GalleryGrid INFO_HEIGHT=0 と仮想スクロールの不整合**
   - `estimateSize()` が 0 を返したとき、`@tanstack/react-virtual` の行高計算が崩れる可能性。
   - 実際には cover だけの高さ（`colWidth * (3/2)` + 8）が返るはずだが、正しく計算されているか確認。

### ⚠️ 中優先

6. **zustand persist と DEFAULT_DISPLAY の不整合**
   - `identifier: false` のデフォルト変更は既存ユーザーの persist state に**効かない**（zustand の仕様）。
   - 新規ユーザーのみ適用される。マイグレーション戦略が必要か確認。

7. **JavLibrary の URL 正規化**
   - 検索リダイレクト後の最終 URL が `?v=` でなく `/ja/?v=` のパターンになる場合の処理。
   - `finalUrl.Contains("?v=")` は `&v=` パターンも考慮しているが、実際のリダイレクト URL パターンとずれている可能性。

8. **Cover ETag の quotes 処理**
   - HTTP 仕様上 ETag は `"value"` のようにダブルクォートで囲む必要がある。
   - 実装: `$"\"{fileInfo.LastWriteTimeUtc.Ticks:x}\""` — ブラウザが正しく `If-None-Match` に返してくるか確認。

---

## Step 9: 報告フォーマット

PASSは不要。FAILのみ報告してください。

```
----------------------
Issue:         （タイトル）
Sprint:        Sprint 2X
Severity:      Critical / High / Medium / Low
Reproduction:  （再現手順）
Expected:      （期待動作）
Actual:        （実際の動作）
Root Cause:    （推測される原因、コードの該当箇所を示す）
Evidence:      （ログ / SQLクエリ結果 / スクリーンショット）
Suggested Fix: （修正方針のみ、コードは書かない）
----------------------
```

Severity 基準:
- **Critical**: データロス / クラッシュ / 全機能停止
- **High**: 特定の機能が使えない / 誤ったデータが保存される
- **Medium**: UX の明らかな劣化 / パフォーマンス問題
- **Low**: 細かいUI崩れ / 軽微な挙動の不一致

---

最後に **Top 5 Critical Issues** と **Top 5 High Issues** のみまとめてください。

コード修正は禁止です。証拠だけ提出してください。
