# Jules QA — Sprint 28: JavLibrary Playwright スクレイピング実証テスト

## ミッション

**JavLibrary プロバイダーが実際の品番から title / actress / cover / maker / label / director / genre を正しく取得できることを、スクレイピング結果の実値とともに証拠提出せよ。**

成功基準:
- N ≥ 20 品番に対してテストを実施
- 各品番でスクレイピングに成功した場合、取得した全フィールドの実値を報告
- 成功・失敗・部分取得を分類し、フィールド別取得率（例: Title 95%, Actress 80%）を集計すること

---

## セットアップ手順

### 1. 依存ツールのインストール

```bash
# Playwright CLI のインストール（初回のみ）
dotnet tool install -g Microsoft.Playwright.CLI

# Chromium バイナリのインストール（初回のみ）
playwright install chromium
```

インストール確認:
```bash
playwright --version
ls ~/.cache/ms-playwright/  # Linux/Mac
# または %LOCALAPPDATA%\ms-playwright\  # Windows
```

### 2. JavLibrary プロバイダーを有効化

`src/WISE.Api/appsettings.json` の `JavLibrary.IsEnabled` を一時的に `true` に変更:

```json
"JavLibrary": { "Priority": 45, "IsEnabled": true }
```

また、`RateLimiter.Domains` に JavLibrary のレート設定を追加:

```json
"www.javlibrary.com": { "RequestsPerSecond": 0.5, "BurstSize": 2 }
```

（Cloudflare ブロック回避のため低めに設定。0.5 req/s = 2秒に1リクエスト）

### 3. API サーバー起動

```bash
cd src/WISE.Api
dotnet run
# または dotnet run --urls "http://localhost:5162"
```

---

## テスト対象品番リスト（N=25）

以下の品番を使用すること。有名タイトルを中心に選定しており、JavLibrary に収録されている可能性が高い:

```
SONE-001   SONE-100   SONE-200
ABW-001    ABW-100    ABW-200
MIRD-001   MIRD-100
SSNI-001   SSNI-500   SSNI-999
MIDV-001   MIDV-100   MIDV-200
OFJE-001   OFJE-100   OFJE-200   OFJE-300
IPX-001    IPX-100    IPX-500
PRED-001   PRED-100   PRED-200
DASS-001
```

---

## テスト方法

### 方法A: FetchMetadata API 経由（推奨・最も本番に近い）

#### A-1. ワーク登録 → メタデータ取得

品番ごとに以下の手順を繰り返す:

```bash
# 1. ワーク登録（ダミーファイルパスで可）
WORK_ID=$(curl -s -X POST http://localhost:5162/api/works \
  -H "Content-Type: application/json" \
  -d '{"filePath": "/tmp/test.mp4", "mediaType": 1}' | jq -r '.id')

echo "WorkId: $WORK_ID"

# 2. メタデータ取得ジョブをキュー
JOB_ID=$(curl -s -X POST http://localhost:5162/api/works/$WORK_ID/fetch-metadata \
  | jq -r '.jobId')

echo "JobId: $JOB_ID"

# 3. ジョブ完了を待機（30秒タイムアウト）
for i in {1..30}; do
  STATUS=$(curl -s http://localhost:5162/api/jobs/$JOB_ID | jq -r '.status')
  echo "[$i] Status: $STATUS"
  [ "$STATUS" = "Completed" ] || [ "$STATUS" = "Failed" ] && break
  sleep 2
done

# 4. 取得されたメタデータを確認
curl -s http://localhost:5162/api/works/$WORK_ID | jq '{
  title: .title,
  primaryIdentifier: .primaryIdentifier,
  metadata: [.metadataFields[] | {field: .fieldName, value: .value, provider: .providerId}]
}'
```

**注意**: JavLibrary は他のプロバイダーとの競合で上書きされる場合がある。  
確認は `metadataFields` の `providerId == "JavLibrary"` のフィールドを対象にすること。

#### A-2. JavLibrary 単体テスト用スクリプト

他プロバイダーを無効化した appsettings を使うか、以下の方法で JavLibrary の結果を直接観察する。

`appsettings.Development.json` を作成:

```json
{
  "MetadataProviders": {
    "Fanza":      { "IsEnabled": false },
    "Mgs":        { "IsEnabled": false },
    "Fc2":        { "IsEnabled": false },
    "DLSite":     { "IsEnabled": false },
    "JavBus":     { "IsEnabled": false },
    "AvWiki":     { "IsEnabled": false },
    "JavLibrary": { "Priority": 45, "IsEnabled": true }
  }
}
```

これで FetchMetadata 結果が JavLibrary 単体からの取得になる。

### 方法B: 直接スクレイピング確認スクリプト（補助）

C# スクリプトとして以下を `TestJavLibrary.cs` に作成して実行:

```csharp
// dotnet script TestJavLibrary.cs — または xunit テストとして実装
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using WISE.Infrastructure.Http;

var services = new ServiceCollection()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug))
    .AddSingleton<PlaywrightBrowserService>()
    .BuildServiceProvider();

var playwright = services.GetRequiredService<PlaywrightBrowserService>();

var identifiers = new[] {
    "SONE-001", "ABW-001", "SSNI-001", "MIDV-001", "OFJE-001",
    "IPX-001", "PRED-001", "DASS-001", "MIRD-001", "SONE-100"
};

foreach (var id in identifiers)
{
    var url = $"https://www.javlibrary.com/ja/vl_searchbyid.php?keyword={id}";
    var html = await playwright.FetchHtmlAsync(url, "#video_title, div.video",
        CancellationToken.None);
    
    Console.WriteLine($"\n=== {id} ===");
    Console.WriteLine($"HTML取得: {(html != null ? $"OK ({html.Length} chars)" : "FAIL")}");
    
    if (html != null)
    {
        // タイトル確認
        var titleMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<h3[^>]*>(.*?)</h3>");
        Console.WriteLine($"Title: {titleMatch.Groups[1].Value}");
        
        // CF通過確認
        var isCF = html.Contains("Just a moment") || html.Contains("cf-browser-verification");
        Console.WriteLine($"Cloudflare blocked: {isCF}");
    }
    
    await Task.Delay(3000); // 礼儀正しいクロール
}

await playwright.DisposeAsync();
```

---

## 報告フォーマット

### 1. セットアップ確認

| 項目 | 結果 |
|------|------|
| playwright install chromium 成功 | ✅/❌ |
| Chromium バージョン | |
| API サーバー起動成功 | ✅/❌ |
| JavLibrary IsEnabled=true 確認 | ✅/❌ |

### 2. 品番別スクレイピング結果（全N件）

各品番について以下を報告:

```
品番: SONE-001
取得結果: 成功 / 失敗 / 部分取得

  Title    : [実際に取得した値]
  Cover URL: [https://...]
  Actress  : [取得した女優名（複数の場合は列挙）]
  Maker    : [取得値]
  Label    : [取得値]
  Director : [取得値]
  Genre    : [取得したジャンル（複数の場合は列挙）]
  ReleaseDate: [取得値]
  Duration : [取得値]

  ソースURL: https://www.javlibrary.com/ja/?v=XXX
  経過時間 : X.Xs
  備考     : （Cloudflareに遮られた等、問題があれば記載）
```

### 3. フィールド別取得率

| フィールド | 取得成功数 / 試行数 | 取得率 |
|------------|---------------------|--------|
| Title | / 25 | % |
| Cover | / 25 | % |
| Actress | / 25 | % |
| Maker | / 25 | % |
| Label | / 25 | % |
| Director | / 25 | % |
| Genre | / 25 | % |
| ReleaseDate | / 25 | % |
| Duration | / 25 | % |

### 4. Cloudflare / キャッシュ動作確認

- 初回リクエスト: Cloudflare チャレンジに遮られたか？
- 同一URLを2回リクエストした場合、キャッシュ（HttpCaches テーブル）から返ったか？
  - 2回目の応答時間が著しく短ければキャッシュヒット
- `HttpCaches` テーブルに行が増えているか？  
  ```sql
  SELECT url, length(body), cached_at, expires_at FROM HttpCaches WHERE url LIKE '%javlibrary%' LIMIT 10;
  ```

### 5. レートリミッター動作確認

- 連続リクエスト時に適切なウェイトが入っているか（ログで `[RateLimiter]` 系のログが出ているか確認）
- 429 などでブロックされた場合、Polly retry が効いているか

### 6. 問題点・懸念事項

以下の点について特に報告すること:

1. **Cloudflare 突破率**: 全リクエストのうち何%が通過できたか
2. **検索ヒット精度**: 検索結果が別品番になっていないか（品番完全一致チェック）  
   例: `SONE-001` で検索して `SONE-0001` や `SON-001` が返ってきていないか
3. **複数ヒット時の選択**: 検索結果が複数ある場合、先頭を選んでいるが正しいか
4. **HTML パース精度**: タイトルに余分な文字列（ `ID:` プレフィックス等）が含まれていないか
5. **Playwright メモリリーク**: 20件処理後もブラウザプロセスが正常に動作しているか
6. **並行実行**: 複数の FetchMetadata ジョブを同時にキューした場合（5件同時）、SemaphoreSlim が正しく機能してクラッシュしないか

---

## 追加検証: プロバイダー優先度チェーン

実運用イメージ（公式 → JavLibrary → 動画系スクレイパー）を確認するため:

1. FANZA に存在する品番（例: `SONE-001`）を FetchMetadata した場合、FANZA の結果が JavLibrary を上書きするか確認
2. FANZA に存在しない品番（FC2 系など）に対して JavLibrary がフォールバックとして機能するか確認
3. `MetadataConflictResolver` がどのプロバイダーの値を採用したかを `metadataFields[].providerId` で確認

---

## ログ確認コマンド

```bash
# API ログで JavLibrary の動作を追う
# [Playwright] ログ: ブラウザ操作
# [JavLibrary] ログ: フェッチ・パース結果
# [RateLimiter] ログ: 待機時間
dotnet run 2>&1 | grep -E "\[Playwright\]|\[JavLibrary\]|\[RateLimiter\]"
```

---

## 合格基準

- **必須**: N=20以上の品番でテスト実施、Cloudflare 突破率 ≥ 80%
- **必須**: Title の取得率 ≥ 70%（タイトルが取れなければ他フィールドも無意味）
- **必須**: Actress の取得率 ≥ 50%（主目的フィールド）
- **必須**: 同一URL の2回目リクエストがキャッシュから返ること（経過時間で判断）
- **任意**: Cover URL が実際に画像として返ること（curl で確認）

**特に不安な点・FBが欲しい点**:
- `waitForSelector: "#video_title, div.video"` の OR 構文が Playwright 1.61.0 で正しく動作するか
- `FetchHtmlAsync` が null を返すケースで JavLibrary 全体がクラッシュせず `MetadataResult.Failed` を正しく返すか
- `PlaywrightBrowserService` が ASP.NET Core DI から Singleton として正しく Dispose されるか（`IAsyncDisposable` hook）
- 20件連続処理でブラウザプロセスがリークまたはクラッシュしないか
