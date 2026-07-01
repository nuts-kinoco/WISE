# Jules QA — Sprint 28: JavLibrary Playwright スクレイピング実証テスト

## ミッション

**JavLibraryプロバイダーが実際の品番からtitle / actress / cover / maker / label / director / genreを正しく取得できることを、スクレイピング結果の実値とともに証拠提出せよ。**

成功基準:
- N ≥ 20品番でテストを実施
- 各品番で取得した全フィールドの実値を報告
- フィールド別取得率（例: Title 95%, Actress 80%）を集計

---

## セットアップ

### 1. Playwright Chromiumインストール（初回のみ）

```bash
# Chromiumバイナリのインストール
~/.dotnet/tools/playwright install chromium
# または
playwright install chromium
```

確認:
```bash
playwright --version
```

### 2. appsettings.json を編集してJavLibraryを有効化

`src/WISE.Api/appsettings.json` を以下のように変更:

```json
{
  "MetadataProviders": {
    "Fanza":      { "Priority": 80, "IsEnabled": false },
    "Mgs":        { "Priority": 70, "IsEnabled": false },
    "Fc2":        { "Priority": 60, "IsEnabled": false },
    "LocalNfo":   { "Priority": 90, "IsEnabled": false },
    "JavBus":     { "Priority": 40, "IsEnabled": false },
    "JavLibrary": { "Priority": 45, "IsEnabled": true }
  },
  "RateLimiter": {
    "DefaultRequestsPerSecond": 2.0,
    "DefaultBurstSize": 5,
    "Domains": {
      "www.javlibrary.com": { "RequestsPerSecond": 0.5, "BurstSize": 2 }
    }
  }
}
```

JavLibrary**以外のプロバイダーをすべて無効**にすること（結果をJavLibrary単体で観察するため）。

### 3. テスト用ダミーファイルを作成

以下の品番でダミー.mp4ファイルを作成する:

```bash
mkdir -p /tmp/javlib_test
for ID in SONE-001 SONE-100 SONE-200 ABW-001 ABW-100 ABW-200 \
           MIRD-001 MIRD-100 SSNI-001 SSNI-500 SSNI-999 \
           MIDV-001 MIDV-100 MIDV-200 OFJE-001 OFJE-100 \
           OFJE-200 OFJE-300 IPX-001 IPX-100 IPX-500 \
           PRED-001 PRED-100 PRED-200 DASS-001; do
  touch "/tmp/javlib_test/${ID}.mp4"
done
ls /tmp/javlib_test/ | wc -l  # 25であること
```

### 4. APIサーバー起動

```bash
cd src/WISE.Api
dotnet run
```

起動ログに `[Playwright] Chromium 起動完了` が出ることを確認（最初のFetchMetadata実行後に出る）。

---

## テスト手順

### Step 1: ダミーファイルをImport

```bash
curl -s -X POST http://localhost:5162/api/jobs/import \
  -H "Content-Type: application/json" \
  -d '{"JobType": "Import", "Payload": {"directoryPath": "/tmp/javlib_test", "mediaType": 1}}' \
  | jq '{jobId: .jobId}'
```

Importジョブの完了を待つ:

```bash
JOB_ID="<上で返ったjobId>"
for i in $(seq 1 30); do
  STATUS=$(curl -s http://localhost:5162/api/jobs/$JOB_ID | jq -r '.status')
  echo "[$i] $STATUS"
  [ "$STATUS" = "Completed" ] || [ "$STATUS" = "Failed" ] && break
  sleep 2
done
```

### Step 2: WorkのIDをAPIから取得（**sqlite3から取得しないこと**）

```bash
# APIのレスポンスからIDを取得する
curl -s "http://localhost:5162/api/works?pageSize=100" | jq '{
  total: .totalCount,
  sample: [.items[:3][] | {id: .id, identifier: .primaryIdentifier}]
}'
```

`total` が25以上であることを確認。0の場合はImportが別DBに書いた可能性がある（後述のトラブルシュート参照）。

### Step 3: 全WorkにFetchMetadataを一括キュー登録

```bash
# APIレスポンスからIDを配列として取得してバッチキュー
WORK_IDS=$(curl -s "http://localhost:5162/api/works?pageSize=100" | jq '[.items[].id]')
echo "対象件数: $(echo $WORK_IDS | jq length)"

curl -s -X POST http://localhost:5162/api/jobs/fetchmetadata/batch \
  -H "Content-Type: application/json" \
  -d "{\"workIds\": $WORK_IDS}" \
  | jq '{queued: .queued}'
```

### Step 4: 完了を待機

```bash
# FetchMetadataジョブの進捗を監視
watch -n 5 'curl -s http://localhost:5162/api/jobs | jq '"'"'
  [.[] | select(.jobType=="FetchMetadata")] |
  {
    total: length,
    queued: [.[] | select(.status=="Queued")] | length,
    running: [.[] | select(.status=="Running")] | length,
    completed: [.[] | select(.status=="Completed")] | length,
    failed: [.[] | select(.status=="Failed")] | length
  }'"'"
```

全件Completed/Failedになったら次に進む。

### Step 5: 結果収集

```bash
# 全workの取得結果をまとめて出力
curl -s "http://localhost:5162/api/works?pageSize=100" | jq -r '
  .items[] | 
  "=== \(.primaryIdentifier // "(no id)") ===\n" +
  "  Title  : \(.title // "(none)")\n" +
  "  Actress: \(.actress // "(none)")\n" +
  "  Maker  : \(.maker // "(none)")\n" +
  "  Cover  : \(.coverUrl // "(none)")\n"
'
```

特定workの全フィールドをプロバイダー別に確認（抽出された品番のひとつで実行）:

```bash
WORK_ID=$(curl -s "http://localhost:5162/api/works?pageSize=1" | jq -r '.items[0].id')
curl -s "http://localhost:5162/api/works/$WORK_ID" | jq '
  .metadata |
  group_by(.providerId) |
  map({
    provider: .[0].providerId,
    fields: map("\(.fieldName): \(.value)") 
  })
'
```

### Step 6: HttpCachesテーブルでキャッシュ動作を確認

APIが使用しているDBのパスを確認:

```bash
# Linux/Mac
ls -la ~/.config/WISE/wise.db

# Windows (PowerShell)
ls "$env:APPDATA\WISE\wise.db"
```

JavLibraryのキャッシュ行数を確認:

```bash
# Linux/Mac
sqlite3 ~/.config/WISE/wise.db "SELECT count(*), min(cached_at), max(cached_at) FROM HttpCaches WHERE url LIKE '%javlibrary%';"

# 同URLを2回FetchMetadataした場合（同じworkを再度キュー登録して実行）、
# 2回目の応答が著しく速ければキャッシュヒット（Playwright未起動のまま応答）
```

---

## 報告フォーマット

### 1. セットアップ確認

| 項目 | 結果 |
|------|------|
| playwright install chromium 成功 | ✅/❌ |
| Chromiumバージョン | |
| APIサーバー起動成功 | ✅/❌ |
| Importジョブ完了（Work件数） | 件 |
| `GET /api/works` で全Work確認 | ✅/❌ |
| JavLibrary IsEnabled=true | ✅/❌ |
| 他プロバイダー全IsEnabled=false | ✅/❌ |

### 2. 品番別スクレイピング結果（全25件）

```
品番: SONE-001
WorkId: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
取得結果: 成功 / 失敗 / 部分取得

  Title      : [実際に取得した値]
  Cover URL  : [https://... または /api/works/.../cover]
  Actress    : [取得した女優名（複数あれば列挙）]
  Maker      : [取得値]
  Label      : [取得値]
  Director   : [取得値]
  Genre      : [取得したジャンル（複数あれば列挙）]
  ReleaseDate: [取得値]
  Duration   : [取得値]

  SourceUrl: https://www.javlibrary.com/ja/?v=XXX
  経過時間 : X.Xs
  備考     : （Cloudflare遮断、検索0件等があれば記載）
```

### 3. フィールド別取得率

| フィールド | 取得成功数 / 25 | 取得率 |
|------------|-----------------|--------|
| Title | | % |
| Cover | | % |
| Actress | | % |
| Maker | | % |
| Label | | % |
| Director | | % |
| Genre | | % |
| ReleaseDate | | % |
| Duration | | % |

### 4. Cloudflare・キャッシュ動作

- Cloudflare突破率: X / 25件
- Cloudflareに遮断された品番: [一覧]
- HttpCachesテーブルの行数（javlibrary行のみ）: X行
- 同URLの2回目リクエストがキャッシュから返ったか: ✅/❌

### 5. 特に報告してほしい点

1. **Cloudflare突破率**: 全リクエストのうち何%が通過できたか
2. **waitForSelectorの動作**: `"#video_title, div.video"` のOR構文が意図通り機能しているか  
   （APIログの `[Playwright] セレクタ ... がタイムアウト` の有無で確認）
3. **品番マッチ精度**: 検索結果が別の品番になっていないか  
   例: `SONE-001`で検索して`SONE-0001`や関係ない品番が返っていないか
4. **20件連続処理後のブラウザ状態**: プロセスがクラッシュ・リークしていないか  
   （全件完了後も追加でFetchMetadata 5件をキューできるか確認）
5. **FetchMetadata失敗時の挙動**: JavLibraryが`null`を返したとき、ジョブが`Failed`か`Completed（0件）`か

---

## トラブルシュート

### `GET /api/works` が0件を返す

APIが参照するDBは`%APPDATA%\WISE\wise.db`（Windows）または`~/.config/WISE/wise.db`（Linux）。  
sqlite3で確認するときは**同じファイル**を指定すること:

```bash
sqlite3 ~/.config/WISE/wise.db "SELECT count(*) FROM Works;"
```

Importが別DBに書いた場合: APIを再起動せず、同じAPIプロセスに対して再度Importを実行する。

### `{"error": "Work {id} not found."}` が返る

WorkIDを`sqlite3`から取得して貼り付けている場合、フォーマットが一致しないことがある。  
**必ず`GET /api/works`のレスポンスのJSONから`.id`を取得すること。**

### `[Playwright] ブラウザ起動に失敗` がログに出る

```bash
playwright install chromium
```

を再実行し、APIを再起動する。

### FetchMetadataジョブが`Failed`になる

```bash
curl -s http://localhost:5162/api/jobs | jq '
  [.[] | select(.jobType=="FetchMetadata" and .status=="Failed")] |
  .[0] | {id: .id, result: .result}
'
```

`result`フィールドにエラー詳細が入っている。
