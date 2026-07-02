# 開発コンテキスト引き継ぎ — WISE

## 1. 開発中のアプリケーション概要

* **アプリ名/ジャンル**: WISE — ローカルメディアライブラリ管理・視聴アプリ（動画/コミック/書籍/写真集/画像集/音声を横断管理）
* **主要な機能群（現在実装済み）**:
  * ローカルフォルダのスキャン/インポート（Move/Copy、監視フォルダ機能）
  * 品番（識別子）ベースのメタデータ自動取得（多段プロバイダー・並列パイプライン）
  * 作品詳細ページ：カバー画像、出演者/作者/サークル/メーカー/シリーズ/ジャンルタグ、評価、お気に入り、メモ、履歴
  * タグクリックによるライブラリ内検索フィルタ（出演者・作者・サークル・メーカー・レーベル・シリーズ・ジャンル）
  * ホーム（ダッシュボード：続きを見る/最近追加/お気に入り/ランダム）とライブラリ（グリッド/リスト表示、密度切替、ソート）の2ビュー
  * 動画プレイヤー（インメモリキャッシュ、Range Request 対応、先読みバッファ設定、**インポート時automatic faststart化**）
  * コミック/書籍リーダー（マウスホイール対応、2Pモード）
  * ジョブキュー（インポート/メタデータ取得の非同期実行、進捗表示）
  * 設定ページ（テーマ、言語、Cookie管理[FC2/MGS]、リーダー設定、ビデオキャッシュ設定、メンテナンス）
  * 重複作品検出・解消（品番一致/タイトル正規化一致、ユーザーデータマージ）

## 2. 現在の設計思想・仕様（リポジトリ内ドキュメント）

**最初に必ず `Document/` フォルダ配下のMDを読むこと。** 特に以下の順で読むことを推奨：

1. `Document/Architecture.md` / `Architecture_v1.1.md` — 全体アーキテクチャ（DDD 4層構成）。**今回のセッションで実装との乖離箇所に注記を多数追加済み（⚠️マーク）。設計時点の想定と実装の実態が異なる箇所は必ず注記を読むこと**
2. `Document/Domain.md` — ドメインモデル（Work, Asset, MetadataField 等）
3. `Document/Metadata.md` / `Document/Pipeline.md` — メタデータ取得パイプラインの設計思想（Tier制・優先度グループ・並列実行）。**同様に実装との乖離注記あり**
4. `Document/Database.md` — DBスキーマ（SQLite/EF Core）
5. `Document/API.md` — APIエンドポイント仕様。**今回のセッションで大幅に実態に合わせて修正済み（旧: Soft Delete表記→物理削除に訂正、存在しないエンドポイント削除、未記載だった実装済みエンドポイント多数追加）**
6. `Document/UI.md` — UI/UX方針
7. `Document/Identifier.md` / `Document/RuleEngine.md` / `Document/Plugin.md` — 識別子解決とプラグイン機構
8. `Document/Roadmap.md` / `Document/ImplementationPlan.md` — 今後の計画
9. **`Document/DeepReview_Sprint30.md`** — Opus/Fable向け横断タスクプロンプト集（P1〜P4、下記参照）
10. **`Document/Review_P1_DbContextRefactoring.md` 〜 `Review_P4_DocDriftAudit.md`** — 上記4プロンプトの分析成果物。実装状況は本ファイル§4参照
11. **`Document/JulesQA_Sprint30.md`** — 今回のリファクタ検証用Julesプロンプト（Prompt A〜F）。実行状況は§4/§5参照

また `MEMORY.md`（`C:\Users\nat\.claude\projects\...\memory\MEMORY.md`）に「WISE v2 Product Constitution」という最上位ルール（Metadata First / Work First / 引き算の美学）へのリンクがあるので、プロダクト判断に迷ったら参照すること。

## 3. 技術スタック・環境

* **フロントエンド**: Next.js **16**（React 19）+ Tailwind CSS、`src/wise-web/`（旧HANDOFFは15と誤記していたため訂正）
  * ※ `src/wise-web/AGENTS.md` に注意書きあり："This is NOT the Next.js you know" — 学習データと異なるAPIがあるため `node_modules/next/dist/docs/` を確認してから実装すること
* **バックエンド**: ASP.NET Core 8（C#）、DDD 4層構成（Api / Application / Domain / Infrastructure）
* **インフラ/DB**: SQLite + EF Core 8（WALモード）
* **その他**:
  * Playwright（Cloudflare回避スクレイピング用）
  * FFmpeg: `C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg.exe`（サムネイル生成・動画faststart化remuxで使用、`FFmpegThumbnailService`/`VideoFastStartService`にハードコードされたパス。ポータビリティなし、dev環境固有）
  * **FFprobe**: `C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg-master-latest-win64-gpl\bin\ffprobe.exe`（アプリコードからは未使用。動画ファイルの構造診断に便利、今回のカクつき調査で発見・活用した）
* **開発環境**: Windows 11、PowerShell（プライマリ）、Bash tool併用可
* **起動方法**: `.claude/launch.json` に定義済み
  * API: "WISE API (ASP.NET Core)" — port 5162
  * Web: "WISE Web (Next.js)" — port 3000
  * `preview_start` ツールで起動（Bashで直接起動しない）

## 4. 直近のチャットで完了したこと（これまでの経緯）

### ★今回セッションの作業: Deep Review Sprint 30（P1〜P4）+ Jules QA + 動画カクつき調査

前回セッションで `Document/DeepReview_Sprint30.md`（Opus/Fable向け4プロンプト）を作成し、今回のセッションで**全て実装・修正まで完了**した。対象コミット範囲: `2b9a877` 〜 `92a04b8`。

#### P1: WiseDbContext直接注入の是正（**全Phase完了、11コントローラー全て是正済み**）
- `Review_P1_DbContextRefactoring.md` の分析に基づき、**読取=QueryService／書込=UseCase**パターンを確立
- Phase1: `HistoryController`/`WatchFoldersController` — パターン確立
- Phase2: `HomeController`/`SettingsController`
- Phase3: `CollectionsController`/`ReaderController`/`AssetsController`
- Phase4: `JobsController`/`DuplicatesController`（**`DuplicateResolveUseCase`にトランザクション境界を追加**：旧実装はループ内で対象ごとに個別SaveChangesしており部分適用リスクがあった。N+1解消も同時実施）
- Phase5: `WorksController`（819行→384行に解体）。`IWorksQueryService` + 4 UseCase（`WorkUserDataUseCase`/`WorkMetadataUseCase`/`WorkCoverUseCase`/`WorkFileUseCase`）に分割
- 追加是正: `SystemController`（当初のPhase計画から漏れていたのを後で発見・修正。`ISystemHistoryQueryService`/`SystemMaintenanceUseCase`）
- **WorkItemMapper.Map によるAPIレスポンス整形は一貫してコントローラー（Api層）に残す設計**（QueryServiceは生のWorkエンティティ or 近い形のDTOを返す）という境界の引き方で全Phase統一

#### P2: メタデータ収集の終了条件をProvider特性ベースに再設計（**完了＋重大バグ修正**）
- `IMetadataProvider.ProvidableFields`宣言を追加（デフォルトnull=制約なし）。Fc2/Fc2Altが`{"Title"}`のみ供給可能と宣言
- `MetadataService.GetExitFields`を識別子文字列分岐から「成功したProviderのProvidableFields積集合」ベースに書き換え
- **【重大バグ・Jules QA Prompt Dで発見→修正済み】**: FANZA/MGS/JavBus/JavLibraryが`CanHandle`未実装（全識別子対応）だったため、FC2識別子（例: `FC2-PPV-4409072`）に対しても実行され、`FanzaMetadataProvider.ConvertToCid`のフォールバック処理（`"-"`除去+小文字化）が`"fc2ppv4409072"`のような一見有効なCID文字列を機械的に生成→無関係な商品ページに誤マッチする恐れがあった。さらにP2の新ロジックでは「制約なしProviderが1件でも成功すると全フィールド待ちになる」ため、FANZAの誤成功がFC2の早期終了を機能不全にしていた（Prompt Bのテストがハングしていた真因でもあった）
  - **修正**: 上記4Providerに`CanHandle(id) => !id.StartsWith("FC2")`を追加（コミット`1b0e36a`）
  - 実データ検証済み: FC2-PPV-4920823のスキャンが以前は全Tier実行（長時間）だったのが、修正後は**0.5秒でFc2→Fc2Altのみ実行**して完了

#### P3: EF Core最適化（**主要項目完了**）
- 監査時に即修正した分（`4d1ca9f`）: `HomeController`の`Task.WhenAll`並行DbContextクエリを逐次awaitに、`JobsController`のN+1解消、各所`AsNoTracking`付与
- **B-1**（`8643684`）: `DuplicatesQueryService.GetDuplicateGroupsAsync`を2段構えに最適化。品番重複はSQL側GroupBy、タイトル重複はTitleのみの軽量射影で候補抽出後、実際に重複と判定されたWorkIdのみMetadataFields+Assetsをフルロード
- **B-3**（`c88738c`）: `WorksQueryService.GetListAsync`/`GetRelatedAsync`をfiltered Includeに変更。`WorkItemMapper.Map`が実際に使う24フィールド・カバー系Assetのみに絞り込み
- C-3: SystemController是正時に射影と併用された冗長Includeを削除
- 実SQLiteプロバイダでの統合テストを多数追加（EF Core InMemoryプロバイダは実SQL変換を検証しないため、`SqliteConnection("Data Source=:memory:")`で接続維持するパターンを確立。`DuplicatesQueryServiceTests`/`WorksQueryServiceTests`参照）

#### P4: ドキュメント乖離の修正（**完了**、判断が必要な項目は現状維持のまま明記）
- `e5f421a`にて`Architecture.md`/`API.md`/`Metadata.md`/`Pipeline.md`を実装の実態に合わせて修正
- 方針: 実装をドキュメントに合わせるのではなく、**ドキュメントを実装の実態に正直に合わせる**。設計判断が必要な項目（後述の§5「未決定の設計判断」参照）は「未決定」と明記して現状維持のまま残した

#### Jules QA Sprint 30（`Document/JulesQA_Sprint30.md`、Prompt A〜F）
- Prompt A（ホーム/設定）: 合格
- Prompt B（コレクション/リーダー/アセット）: 合格（再検証済み。初回ハングの原因はPrompt D関連のFC2誤マッチバグで、修正後に再実行し正常完了）
- Prompt C（ジョブ/重複解消）: **最終報告が未確定**。Jules側でドラフト報告（「合格項目が空欄」の不完全な報告）が提示されたのみで、こちらから完了確認の返信をしていない
- Prompt D（メタデータパイプライン）: 上記P2の重大バグをここで発見。修正後の**再検証をJulesに依頼中**（`Document/JulesQA_Sprint30.md`のPrompt D節に再検証依頼を追記済み、`8d0621a`）
- Prompt E（履歴/監視フォルダ+横断確認）: 合格
- Prompt F（WorksController解体、Phase5対応で新規追加）: Jules側で手動テスト完了、2点の指摘あり
  - 「OpenFolderがLinuxで500エラー」→ **バグではない**（`Windows専用`と明記された仕様。HANDOFF環境もWindows 11のみ。JulesのLinux検証環境固有の制約で、本体には取り込まない判断済み）
  - 「PatchMetadataがCompleted状態では更新しない」→ **不正確な観察**（`ProcessingStatus`enumに`Completed`という値自体が存在せず、`Work.UpdateStatus`にも条件分岐なし。実害なし）
  - Jules側が最終報告（正式フォーマット）を出すかどうか確認を求めてきたところで会話が別件に移った。**最終報告の受け取り・要否確認が未完了**

#### 動画再生時のカクつき問題（**根本原因を特定・診断確定。インポート時の恒久対策は実装済み、既存ファイルは未対応**）
- ユーザー報告: NCYF-023（1920x1080, ~5767kbps, 2:06:52）はカクつく、PFES-058（同スペック帯, 1:59:25）はスムーズ。ビットレート等はほぼ同一なのに挙動が違う
- **原因特定**: ffprobe（上記パス）で内部box構造を直接解析した結果、**moov atom（再生に必須のタイミング/オフセット情報）の位置**が全く違うと判明
  - PFES-058: `ftyp`直後（8.8MB地点）にmoov = faststart構造
  - NCYF-023: moovがファイル末尾（約5.7GB地点）= 非faststart構造
  - `VideoStreamCache`は**ファイル先頭32MBしかメモリキャッシュしない**ため、非faststartファイルはmoov読み取りのたびに巨大ファイル末尾へのHDD物理シークが常に発生 → 再生開始の遅延・再生中の間欠的な引っかかりの根本原因
  - **WISE側のキャッシュ/配信ロジックのバグではなく、動画ファイル自体の作り（hhd800.com等のリップ手法の違い）に起因**することを確認済み
- **対応方針をユーザーに確認**: 「NCYF-023を今すぐfaststart化」「インポート時の自動faststart化」「既存ライブラリ全体の一括診断・修正機能」の3択を提示し、**「インポート時の自動faststart化」のみ選択**された（既存ファイルには触れない方針）
- **実装済み**（`92a04b8`）:
  - `VideoFastStartService`（Infrastructure/Services）新規実装。`IsFastStart(path)`はffprobe起動不要の軽量box header解析（moov/ftyp/mdat、64bit largesize box対応）で判定。非faststartと判定されたら`ffmpeg -c copy -movflags +faststart`でコンテナのみ書き換え（再エンコードなし・ロスレス）
  - `ExecuteImportJobUseCase`のMove/Copy直後・Asset登録前にfaststart化処理を挿入。remux後にファイルサイズが変わりうるため`Asset.FileSize`も再取得するよう修正
  - **テスト中に実際のバグを発見・修正**: 一時ファイル名を`{path}.faststart.tmp`にしていたところ、ffmpegが`.tmp`拡張子から出力コンテナ形式を自動判定できずエラーになっていた。拡張子を維持した命名（`{name}.faststart_tmp{ext}`）に変更して解消。**実際にffmpegを動かして検証していなければ見逃していた不具合**
  - テスト5件追加、実ffmpeg往復・実ライブラリファイル（NCYF-023/PFES-058）での判定一致を含めて全て合格
- **未対応**: NCYF-023自体は非faststartのままユーザーが明示的にスコープ外とした。既存ライブラリの一括診断・修正機能も未実装（ユーザーが選ばなかった選択肢）

### メタデータ取得パイプライン刷新（旧セッションの成果、現在も有効）
* Tier制の並列スクレイピングパイプライン構築：Tier1=FANZA, Tier2=MGS, Tier3=JavLibrary+JavBus（並列）, Tier4=AdultWiki+AvWiki（並列）
* `MetadataService` を二段階収集方式に書き換え：Phase1でテキストフィールド（早期終了、**今回P2で識別子分岐からProvider特性ベースに再設計済み**）、Phase2でカバー画像品質が閾値未満なら残りTierへドリルダウン
* カバー画像品質閾値125KB（`FetchMetadataJobUseCase`）
* AdultWikiMetadataProviderの改善：URLパラメータからの女優名抽出、タイトル抽出精度向上、シリーズ抽出の重複排除

### UI/UXバグ修正（旧セッションの成果、現在も有効）
* ジョブキューパネルが「処理中」のまま消えない問題を修正
* サーバー起動時のゾンビジョブ自動リセット処理を追加
* 「ファイルの場所」ボタンが動作しない問題を修正
* インポートページの入力パスがページ遷移で消える問題を修正
* Readerのマウスホイール対応、2Pモードの白い線除去

### 動画キャッシュ機能（旧セッションの成果、今回セッションでfaststart対応を追加）
* `VideoStreamCache`（Infrastructure層シングルトン）：ファイル先頭最大32MBをLRUキャッシュ、`MaxMb`設定可能（デフォルト1024MB）
* `AssetsController`（Phase3で`IAssetsQueryService`経由に是正済み）にRange Request処理を実装
* **今回追加**: `VideoFastStartService`によるインポート時の自動faststart化（上記参照）

### 検索・タグ表示の修正（旧セッションの成果、現在も有効）
* 複数女優作品で `ActressTag` に格納される仕様への対応
* 検索対象フィールドに `ActressTag` と `Tag` を追加
* コミック作品向けに「作者」「サークル」タグを詳細ページに追加表示
  * **重要な既知の罠**（今回`Document/Metadata.md`に正式明文化済み）: フィールド名の大小文字不一致。Video系Provider（Fanza/Mgs/JavBus/Fc2等）は`Actress`/`Maker`等PascalCase、Comic系Provider（DoujinishiFilename/DLSite/Fanza同人経路）は`author`/`circle`等lowercase。クライアント側（`WorkItemMapper`）は両方フォールバックする実装になっている
* 詳細ページからタグクリック→ホームに戻ると検索クエリがセットされた状態でライブラリビューへ自動切り替え

## 5. 次に取り組むタスク / 目的

* **今回のチャットでのゴール**: 未定（ユーザーからの次の指示待ち）

* **要フォローアップ（優先度高）**:
  1. **APIサーバーの再ビルド・生存確認が必要**: 今回セッションの後半（SystemController是正・P3-B1・P3-B3・VideoFastStartService統合の4コミット分）は、APIサーバーがJulesの並行QAセッションでロックされていたため**`WISE.Api`単体のビルド確認・実APIでの動作確認が未実施**。次セッション開始時にまず `netstat -ano | findstr :5162` でプロセス生存確認→安全なら再起動して `dotnet build src/WISE.Api` が通ることを確認すること
  2. **実インポートでのfaststart化の動作確認**: `VideoFastStartService`はUnitテスト（合成動画+実ffmpeg往復）では検証済みだが、実際に`ExecuteImportJobUseCase`経由でインポートを実行してエンドツーエンドで動作するかは未確認
  3. **Julesの最終報告待ち**:
     - Prompt C（ジョブ/重複解消）: ドラフト報告のみで正式な最終報告を受け取っていない。催促が必要
     - Prompt D（メタデータパイプライン）: P2バグ修正後の再検証を依頼中、結果待ち
     - Prompt F（WorksController解体）: 最終報告を出すか確認された状態で止まっている。Go出しが必要

* **未決定の設計判断（P4監査で発見、ユーザー判断待ち）**:
  * `Document/Architecture.md`に「未決定」として明記済みの以下の項目、対応するかどうかは未定：
    1. **FTS5**: 仮想テーブル・更新機構は存在するが検索には未接続（現状LIKE検索のみ）。接続するか、FTS5機構自体を廃止するか
    2. **AssetRole/AssetType併存**: `Asset`エンティティに両プロパティが存在し未収斂。どちらかに統合するか
    3. **Soft Delete化**: 現状Work削除は物理削除。Soft Delete化するか
    4. **Collection Type**: 8種類の設計（Author/Circle/Person/Series/Maker/Favorite/Playlist/SmartFolder）だが実装は無差別のPlaylist機能のみ。実装するか設計書側を諦めるか

* **既存ライブラリのfaststart対応（ユーザーが今回スコープ外とした項目）**:
  * NCYF-023.mp4は非faststartのまま。単体で直したい場合は`ffmpeg -i input.mp4 -c copy -movflags +faststart output.mp4`後にファイル差し替え
  * ライブラリ全体の一括診断・修正機能が欲しくなったら、`VideoFastStartService.IsFastStart`を流用してスキャンするメンテナンス機能を新規実装する想定

* **要検証・未確認の項目（旧セッションから持ち越し、一部は今回で解決済み）**:
  * ~~動画再生時のカクつき~~ → **今回セッションで根本原因を特定・診断確定**（moov atom位置問題）。インポート時の恒久対策は実装済みだが、既存ファイル（NCYF-023含む）は未対応
  * カバー画像しきい値125KBでのスクレイピング再実行結果の確認（引き続き未確認）
  * HOIZ-017等、個別作品のメタデータ抽出結果の確認（引き続き未確認）

## 6. その他必要と思われる項目

### ビルド・実行時の注意点
* **DLLロックエラー多発**: dotnetプロセスがDLLをロックしたままだとビルド失敗する。ビルド前に `taskkill /F /IM dotnet.exe` と `taskkill /F /IM WISE.Api.exe` を実行してから `dotnet build` すること
  * **⚠️重要な教訓（今回セッションでの実際の事故）**: taskkillは無条件に実行すると、**別セッション（Jules等）が使用中のAPIサーバーを誤って落とす**リスクがある。実際に今回、稼働中のAPIサーバーをtaskkillで落としてしまい緊急復旧する事態が発生した。**taskkill前に必ず `netstat -ano | findstr :5162` でプロセスの生存・使用状況を確認し、他セッションが使っている可能性を考慮すること**。ビルドがDLLロックで失敗する場合でも、慌てて稼働中サーバーをkillせず、まずビルド確認は実SQLiteを使った単体テスト（`dotnet test`は別プロセスなので通常ロックされない）で代替できないか検討する
* PowerShellでは `tail`, `head` 等のUnixコマンドは使えない。`Select-Object -Last N` を使う
* プレビューサーバーは `preview_start` ツール経由で起動する（`.claude/launch.json` の設定名を正確に指定。"wise-web"ではなく"WISE Web (Next.js)"）
* **EF Core InMemoryプロバイダは実SQL変換を検証しない**: filtered Include・GroupBy+ToUpper・Contains(list)等、実際にSQLへ変換可能かを検証したい場合は`Microsoft.Data.Sqlite`の`Data Source=:memory:`で接続を維持する統合テストパターンを使うこと（`DuplicatesQueryServiceTests.cs`/`WorksQueryServiceTests.cs`が参考実装）

### コードベースの設計パターン
* Priority値によるグループ化で並列/直列のTier制御を実現（同一Priority = 並列実行グループ）
* `IMetadataProvider.CanHandle(identifier)` デフォルト実装は `true`（全識別子対応）。**FC2/Fc2Altは`^FC2`のみ許可、逆にFanza/Mgs/JavBus/JavLibraryは`FC2`で始まる識別子を明示的に除外**（今回のP2バグ修正で追加。「Provider は自分の対応可能領域を宣言する」という設計原則がここで初めて双方向に適用された）
* `IMetadataProvider.ProvidableFields`（今回P2で新設）: Provider自身が「確実に供給できるフィールド」を宣言し、`MetadataService`の早期終了判定に使う。nullは制約なし（全フィールド対応とみなす）
* MetadataField は `FieldName`（キー） + `Value` + `IsPrimary`（複数値のうちどれを代表値とするか）の構造
* Genre等の複数値フィールドは `|` 区切り文字列として1レコードに格納される場合がある（フロントで `split("|")` して展開）
* **P1で確立したQueryService/UseCaseパターン**: コントローラーからは`WiseDbContext`を直接注入せず、読取は`Application/Queries`のインターフェース+`Infrastructure/Data/Queries`の実装、書込は`Api/UseCases`のクラスに委譲する。新規コントローラー・新規エンドポイントを追加する際はこのパターンに従うこと

### 未着手・検討中と思われる領域（Roadmap.md等要確認）
* Document配下の `Roadmap.md` / `ImplementationPlan.md` に将来計画が記載されているはずなので、次のタスク選定前に必ず目を通すこと
* 上記§5「未決定の設計判断」4項目
