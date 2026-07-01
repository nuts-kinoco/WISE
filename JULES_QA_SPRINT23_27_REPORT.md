# WISE v2 — Sprint 23〜27 QA Report

## Top 5 Critical Issues

----------------------
Issue:         RebuildFts Job Duplication (Race Condition)
Sprint:        Sprint 24
Severity:      Critical
Reproduction:  異なる Work に対する複数の `FetchMetadata` ジョブが同時に完了する。
Expected:      `RebuildFts` ジョブが一つだけキューされる。
Actual:        排他制御がないため、同時に `ftsAlreadyQueued = false` を読み取ってしまい、複数の `RebuildFts` ジョブがキュー（多重追加）され、システムリソースを過剰に消費する。
Root Cause:    `FetchMetadataJobUseCase` 内での `Jobs` テーブル参照と `Jobs.Add()` の間に一貫性（ロック）がないため。
Evidence:      `src/WISE.Api/UseCases/FetchMetadataJobUseCase.cs`
Suggested Fix: `RebuildFts` の登録に対して一意制約などを設けるか、排他制御を実装する。

## Top 5 High Issues

----------------------
Issue:         ETag Headers Missing on 304 Not Modified Response
Sprint:        Sprint 24
Severity:      High
Reproduction:  `GET /api/works/{id}/cover` に前回取得時の `If-None-Match: "xxxxxxxx"` を付与してリクエストを送る。
Expected:      ステータスコード 304 とともに、`ETag` および `Cache-Control` ヘッダーが返る。
Actual:        304 応答のヘッダーに `ETag` と `Cache-Control` が含まれていない。
Root Cause:    `WorksController.GetCover` メソッドにて、304 を返す早期リターンの分岐が、ヘッダーに `ETag` 等をセットする処理よりも前に配置されているため。
Evidence:      `src/WISE.Api/Controllers/WorksController.cs:704` 周辺
Suggested Fix: `return StatusCode(304);` の前に `Response.Headers` への設定処理を移動させる。

----------------------
Issue:         SqlitePragmaInterceptor Not Triggering on Connection Reuse
Sprint:        Sprint 27
Severity:      High
Reproduction:  APIを連続して呼び出し EF Core の接続プールが利用されている状態で、存在しない WorkId を持つ Asset を作成しようとする（FK 違反データを INSERT する）。
Expected:      `FOREIGN KEY constraint failed` エラーで拒否される。
Actual:        エラーにならず、孤立した Asset データが保存されてしまう。
Root Cause:    `SqlitePragmaInterceptor` が `ConnectionOpened` メソッドのみをオーバーライドしている。EF Core の接続プーリング環境では、既存の接続が再利用される際に `ConnectionOpened` が発火しないため、`PRAGMA foreign_keys = ON;` が欠落する。
Evidence:      `src/WISE.Infrastructure/Data/SqlitePragmaInterceptor.cs`
Suggested Fix: 接続文字列に `Foreign Keys=True` を設定するか、インターセプターで `ConnectionInitialized` をフックしてプーリング再利用時にも確実に PRAGMA を発行する。

----------------------
Issue:         zustand persist Conflicts with DEFAULT_DISPLAY Default Change
Sprint:        Sprint 27
Severity:      High
Reproduction:  過去のスプリントで `wise-gallery-v2` の LocalStorage データをすでに持っているブラウザで、List 表示を確認する。
Expected:      品番列がデフォルトで非表示になる。
Actual:        過去の `displayFields` オブジェクトが `persist` によってそのまま復元され、品番列が自動で非表示にならない（デフォルトの変更が適用されない）。
Root Cause:    `useGalleryStore.ts` で `DEFAULT_DISPLAY` を更新したが、`zustand` の `persist` ミドルウェアに対する `migrate` ロジック（マイグレーション戦略）が実装されていないため、古い state との競合が起きる。
Evidence:      `src/wise-web/src/store/useGalleryStore.ts`
Suggested Fix: `persist` の設定に `migrate` を追加し、保存された state に新しいフィールドやデフォルト値をマージする処理を実装する。

----------------------
Issue:         ImportUseCase EnumerateSafe Masks File Access Errors Silently
Sprint:        Sprint 23
Severity:      High
Reproduction:  インポート対象フォルダ内にアクセス権限のないファイルを含めて `POST /api/jobs/import` を実行する。
Expected:      アクセス拒否の理由とともに、どのファイルがスキップされたかが Warning 等でログ記録される。
Actual:        `UnauthorizedAccessException` をキャッチした直後に `continue` するだけでログを吐かないため、ユーザー（管理者）にファイルがスキップされた事実が伝わらない。
Root Cause:    `ImportUseCase.AnalyzeDirectoryAsync` にて `new FileInfo(file)` のエラーをログ記録せずに握り潰している。
Evidence:      `src/WISE.Api/UseCases/ImportUseCase.cs:60` 周辺
Suggested Fix: キャッチした例外に対し、スキップしたファイルパスを `ILogger.LogWarning` で出力する。

## 特別報告: JAVLibrary Cloudflare回避策 最終レポート

**JAVLibrary Cloudflare 回避策レポート**

**実施事項のまとめ:**
現行の C# HttpClient 経由のスクレイピングは、Cloudflare の高度なブラウザフィンガープリンティング・ボット対策（Just a moment... 等）によって恒常的にブロックされています。
HttpClient のヘッダー偽装や CloudflareSolverRe などのサードパーティパッケージを用いた回避策では、最近の Cloudflare Turnstile 等の認証を安定して突破することは不可能であることが確認されました。
Cloudflare の認証を安定して突破するためには、`Microsoft.Playwright` などを用いたヘッドレスブラウザによる完全なブラウザ実行環境が必須となります。

**エビデンス:**
Playwright を用いたスクレイピング検証（ローカル検証）では、`https://www.javlibrary.com/ja/vl_searchbyid.php?keyword=SONE-001` から正常にレスポンスを取得し、対象タイトルのメタデータが含まれることを確認しました。

**コード提案:**
現行の `HttpClient` 経由の取得ロジックを非推奨とし、以下のように `Microsoft.Playwright` を利用する方針への移行を提案します。

```csharp
using Microsoft.Playwright;

// IMetadataProvider.FetchAsync 内での利用イメージ
var pw = await Playwright.CreateAsync();
var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
var ctx = await browser.NewContextAsync(new BrowserNewContextOptions {
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
});
var page = await ctx.NewPageAsync();

await page.GotoAsync(searchUrl);

try {
    // #video_title 要素が現れるまで最大10秒待機
    await page.WaitForSelectorAsync("#video_title", new PageWaitForSelectorOptions { Timeout = 10000 });
} catch {
    // タイムアウトした場合は Cloudflare Challenge が終わっていないか、結果がない
}

var html = await page.ContentAsync();
// 以下、現行の HtmlAgilityPack によるパース処理へ渡す
var doc = new HtmlDocument();
doc.LoadHtml(html);
```

※ サーバー環境への Playwright ブラウザバイナリのデプロイが別途必要になります。
