# Sprint 28 QA Report: JavLibrary Playwright Scraping

## 1. セットアップ確認

| 項目 | 結果 |
|---|---|
| playwright install chromium 成功 | ✅ |
| Chromiumバージョン | 149.0.7827.55 |
| APIサーバー起動成功 | ✅ |
| Importジョブ完了（Work件数） | 25件 |
| GET /api/works で全Work確認 | ✅ |
| JavLibrary IsEnabled=true | ✅ |
| 他プロバイダー全IsEnabled=false | ✅ |

## 2. 品番別スクレイピング結果（全25件）

全品番で `JavLibrary` プロバイダーによる取得は失敗しました。

```
取得結果: 失敗 (Cloudflareタイムアウト)
経過時間: ~30s
備考: 全てのリクエストで `#video_title, div.video` のセレクタ待機がタイムアウト。CloudflareのTurnstile/JSチャレンジを突破できず。
```

## 3. フィールド別取得率

| フィールド | 取得成功数 / 25 | 取得率 |
|---|---|---|
| Title | 0 | 0% |
| Cover | 0 | 0% |
| Actress | 0 | 0% |
| Maker | 0 | 0% |
| Label | 0 | 0% |
| Director | 0 | 0% |
| Genre | 0 | 0% |
| ReleaseDate | 0 | 0% |
| Duration | 0 | 0% |

## 4. Cloudflare・キャッシュ動作

* Cloudflare突破率: **0 / 25件 (0%)**
* Cloudflareに遮断された品番: **全品番** (SONE-001, SONE-100, SONE-200, ABW-001, ABW-100, ABW-200, MIRD-001, MIRD-100, SSNI-001, SSNI-500, SSNI-999, MIDV-001, MIDV-100, MIDV-200, OFJE-001, OFJE-100, OFJE-200, OFJE-300, IPX-001, IPX-100, IPX-500, PRED-001, PRED-100, PRED-200, DASS-001)
* HttpCachesテーブルの行数（javlibrary行のみ）: **0行**
* 同URLの2回目リクエストがキャッシュから返ったか: **❌** (全て取得失敗したためキャッシュも生成されず)

## 5. 特に報告してほしい点

* **Cloudflare突破率**: 0% (Headless Chromium に stealth init script を適用してもすべてブロックされました)
* **waitForSelectorの動作**: 意図通り機能している。APIログに `[Playwright] セレクタ '#video_title, div.video' がタイムアウト(30s) — Cloudflare未突破か結果なし` が記録されている。
* **品番マッチ精度**: 確認不可 (データ取得できず)
* **20件連続処理後のブラウザ状態**: クラッシュはしていない。3並行 (Semaphore) で順次処理され、最終的にすべてのジョブが完了処理(Failed状態)となった。
* **FetchMetadata失敗時の挙動**: 取得失敗時は `JobStatus = Failed` (status 4) に遷移することを確認した。
