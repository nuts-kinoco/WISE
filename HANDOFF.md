# 開発コンテキスト引き継ぎ — WISE

## 1. 開発中のアプリケーション概要

* **アプリ名/ジャンル**: WISE — ローカルメディアライブラリ管理・視聴アプリ（動画/コミック/書籍/写真集/画像集/音声を横断管理）
* **主要な機能群（現在実装済み）**:
  * ローカルフォルダのスキャン/インポート（Move/Copy、監視フォルダ機能）
  * 品番（識別子）ベースのメタデータ自動取得（多段プロバイダー・並列パイプライン）
  * 作品詳細ページ：カバー画像、出演者/作者/サークル/メーカー/シリーズ/ジャンルタグ、評価、お気に入り、メモ、履歴
  * タグクリックによるライブラリ内検索フィルタ（出演者・作者・サークル・メーカー・レーベル・シリーズ・ジャンル）
  * ホーム（ダッシュボード：続きを見る/最近追加/お気に入り/ランダム）とライブラリ（グリッド/リスト表示、密度切替、ソート）の2ビュー
  * 動画プレイヤー（インメモリキャッシュ、Range Request 対応、先読みバッファ設定）
  * コミック/書籍リーダー（マウスホイール対応、2Pモード）
  * ジョブキュー（インポート/メタデータ取得の非同期実行、進捗表示）
  * 設定ページ（テーマ、言語、Cookie管理[FC2/MGS]、リーダー設定、ビデオキャッシュ設定、メンテナンス）

## 2. 現在の設計思想・仕様（リポジトリ内ドキュメント）

**最初に必ず `Document/` フォルダ配下のMDを読むこと。** 特に以下の順で読むことを推奨：

1. `Document/Architecture.md` / `Architecture_v1.1.md` — 全体アーキテクチャ（DDD 4層構成）
2. `Document/Domain.md` — ドメインモデル（Work, Asset, MetadataField 等）
3. `Document/Metadata.md` / `Document/Pipeline.md` — メタデータ取得パイプラインの設計思想（Tier制・優先度グループ・並列実行）
4. `Document/Database.md` — DBスキーマ（SQLite/EF Core）
5. `Document/API.md` — APIエンドポイント仕様
6. `Document/UI.md` — UI/UX方針
7. `Document/Identifier.md` / `Document/RuleEngine.md` / `Document/Plugin.md` — 識別子解決とプラグイン機構
8. `Document/Roadmap.md` / `Document/ImplementationPlan.md` — 今後の計画

また `MEMORY.md`（`C:\Users\nat\.claude\projects\...\memory\MEMORY.md`）に「WISE v2 Product Constitution」という最上位ルール（Metadata First / Work First / 引き算の美学）へのリンクがあるので、プロダクト判断に迷ったら参照すること。

## 3. 技術スタック・環境

* **フロントエンド**: Next.js（React 19）+ Tailwind CSS、`src/wise-web/`
  * ※ `src/wise-web/AGENTS.md` に注意書きあり："This is NOT the Next.js you know" — 学習データと異なるAPIがあるため `node_modules/next/dist/docs/` を確認してから実装すること
* **バックエンド**: ASP.NET Core 8（C#）、DDD 4層構成（Api / Application / Domain / Infrastructure）
* **インフラ/DB**: SQLite + EF Core 8（WALモード）
* **その他**: Playwright（Cloudflare回避スクレイピング用）、FFmpeg（サムネイル生成）
* **開発環境**: Windows 11、PowerShell（プライマリ）、Bash tool併用可
* **起動方法**: `.claude/launch.json` に定義済み
  * API: "WISE API (ASP.NET Core)" — port 5162
  * Web: "WISE Web (Next.js)" — port 3000
  * `preview_start` ツールで起動（Bashで直接起動しない）

## 4. 直近のチャットで完了したこと（これまでの経緯）

### メタデータ取得パイプライン刷新
* Tier制の並列スクレイピングパイプライン構築：Tier1=FANZA, Tier2=MGS, Tier3=JavLibrary+JavBus（並列）, Tier4=AdultWiki+AvWiki（並列）
* FC2系識別子のみFC2プロバイダーを実行する `CanHandle(string identifier)` インターフェースメソッド追加
* `MetadataService` を二段階収集方式に書き換え：Phase1でテキストフィールド（早期終了）、Phase2でカバー画像品質が閾値未満なら残りTierへドリルダウン
* カバー画像品質閾値を段階的に引き上げ：8KB → 20KB → 50KB → 150KB → **125KB（現在値）**
* AdultWikiMetadataProviderの改善：URLパラメータからの女優名抽出、タイトル抽出精度向上、シリーズ抽出の重複排除

### UI/UXバグ修正
* ジョブキューパネルが「処理中」のまま消えない問題を修正（ポーリング間隔短縮+自動非表示ディレイ）
* サーバー起動時のゾンビジョブ自動リセット処理を追加
* 「ファイルの場所」ボタンが動作しない問題を修正（動画以外のアセットへのフォールバック）
* インポートページの入力パスがページ遷移で消える問題を修正（localStorage lazy initializer → useEffect方式に変更、SSRエラー回避）
* Readerのマウスホイール対応、2Pモードの白い線除去

### 動画キャッシュ機能（新規実装）
* `VideoStreamCache`（Infrastructure層シングルトン）：ファイル先頭最大32MBをLRUキャッシュ、`MaxMb`設定可能（デフォルト1024MB）
* `AssetsController` にRange Request処理を実装：キャッシュヒット時は206 Partial Contentをメモリから返却、ミス時は`PhysicalFile`でOSゼロコピー送信
* 設定ページ（リーダー/ビデオ）のキャッシュメモリをプルダウン化：256/512/1024/2048/3072/4096 MB

### 検索・タグ表示の修正
* 複数女優作品で `Actress`ではなく`ActressTag`に格納される仕様に対応し、`WorkItemMapper`がホーム/ライブラリ双方で正しく表示するよう修正
* 検索対象フィールドに `ActressTag` と `Tag` を追加（ホーム・ライブラリ双方が同じ `/api/works` エンドポイントを使用するため両対応）
* コミック作品向けに「作者」「サークル」タグを詳細ページに追加表示（クリックで検索フィルタ）
  * **重要な既知の罠**: フィールド名の大小文字不一致に注意。`DoujinishiFilenameMetadataProvider`/`DLSiteMetadataProvider`/`FanzaMetadataProvider` は `"circle"`/`"author"`（小文字）で保存するが、Video系は`"Actress"`（大文字）。クライアント側は両方フォールバックする必要がある
* 詳細ページからタグクリック→ホームに戻ると検索クエリがセットされた状態でライブラリビューへ自動切り替えするよう修正

## 5. 次に取り組むタスク / 目的

* **今回のチャットでのゴール**: 未定（ユーザーからの次の指示待ち）
* **要検証・未確認の項目**:
  * 動画再生時のカクつきが完全に解消したか（Range Requestキャッシュ・PhysicalFile切り替え後の効果測定）— ユーザーからは複数回「まだカクつく」との報告があり、根本原因（HDD読み取り速度 vs アプリ側バッファリング）の切り分けが未完了の可能性
  * カバー画像しきい値125KBでのスクレイピング再実行結果の確認
  * NCYF-023, HOIZ-017等、個別作品のメタデータ抽出結果の確認

## 6. その他必要と思われる項目

### ビルド・実行時の注意点
* **DLLロックエラー多発**: dotnetプロセスがDLLをロックしたままだとビルド失敗する。ビルド前に `taskkill /F /IM dotnet.exe` と `taskkill /F /IM WISE.Api.exe` を実行してから `dotnet build` すること
* PowerShellでは `tail`, `head` 等のUnixコマンドは使えない。`Select-Object -Last N` を使う
* プレビューサーバーは `preview_start` ツール経由で起動する（`.claude/launch.json` の設定名を正確に指定。"wise-web"ではなく"WISE Web (Next.js)"）

### コードベースの設計パターン
* Priority値によるグループ化で並列/直列のTier制御を実現（同一Priority = 並列実行グループ）
* `IMetadataProvider.CanHandle(identifier)` デフォルト実装は `true`（全識別子対応）、FC2のみ正規表現でフィルタ
* MetadataField は `FieldName`（キー） + `Value` + `IsPrimary`（複数値のうちどれを代表値とするか）の構造
* Genre等の複数値フィールドは `|` 区切り文字列として1レコードに格納される場合がある（フロントで `split("|")` して展開）

### 未着手・検討中と思われる領域（Roadmap.md等要確認）
* Document配下の `Roadmap.md` / `ImplementationPlan.md` に将来計画が記載されているはずなので、次のタスク選定前に必ず目を通すこと
