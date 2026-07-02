# Deep Review Sprint 30 — Opus / Fable 向けプロンプト集

> **このドキュメントの位置づけ**
> Jules（1機能単位のバグ修正が得意）では拾いきれない、**複数ファイル・複数レイヤーに跨る設計判断**を要するタスクを、高コスト・高精度モデル（Opus / Fable）に投げるためのプロンプト集。
> 各プロンプトは**独立したセッションで単体投入できる**よう自己完結的に書いてある。コスト効率のため、単純なバグ修正はJulesに残し、ここには「構造を見渡す判断」だけを集約している。
>
> **全プロンプト共通の前提（セッション冒頭に必ず読ませる）**
> - まず `HANDOFF.md` と `Document/Architecture.md` / `Architecture_v1.1.md` / `Domain.md` を読み、DDD 4層構成（Api / Application / Domain / Infrastructure）とドメインモデルを把握すること。
> - `src/wise-web/AGENTS.md` の注意書き（"This is NOT the Next.js you know"）に留意。
> - ビルド前に `taskkill /F /IM dotnet.exe` と `taskkill /F /IM WISE.Api.exe`（DLLロック対策）。
> - プロダクト判断に迷ったら `MEMORY.md` 経由の「WISE v2 Product Constitution」（Metadata First / Work First / 引き算の美学）を参照。
> - **重要な運用ルール**: いきなり全面改修に着手せず、まず**現状分析 → 問題の優先度付き一覧 → 段階的リファクタリング計画**を提示し、承認を得てから実装に入ること。大きな変更は複数の小さなPRに分割する。

---

## Prompt 1 — アーキテクチャ違反の是正：`WiseDbContext`のコントローラー直接注入

**種別**: アーキテクチャ・リファクタリング（横断・高難度）
**担当推奨**: Opus（全体構造の一貫した再設計判断が必要）

### 背景
DDD 4層構成を掲げているにもかかわらず、以下 **11コントローラー** が Infrastructure 層の `WiseDbContext` を直接コンストラクタ注入しており、プレゼンテーション層がデータアクセス実装に直結している（レイヤ違反）。

```
AssetsController, CollectionsController, DuplicatesController, HistoryController,
HomeController, JobsController, ReaderController, SettingsController,
SystemController, WatchFoldersController, WorksController(819行・最大)
```

一方で Domain 層には既に `IWorkRepository`（`GetByIdAsync`/`AddAsync`/`UpdateAsync`/`DeleteAsync`）と実装 `WorkRepository` が存在し、`ProcessNewAssetUseCase` / `FetchMetadataJobUseCase` 等の UseCase 側では正しくリポジトリ経由でアクセスしている。**つまり「あるべき姿」の部品は既にあり、コントローラー群だけが取り残されている**状態。

### 依頼内容
1. **現状マッピング**: 11コントローラーそれぞれが `_db`/`WiseDbContext` をどう使っているか分類する（単純CRUD / 複雑クエリ / 集計 / 生LINQ / トランザクション境界）。特に `WorksController`(819行)、`DuplicatesController`(273行)、`JobsController`(263行) は肥大化しており要注意。
2. **設計判断**: 以下のトレードオフを踏まえ、WISEに最適な是正方針を提案する。
   - 案A: 既存 `IWorkRepository` パターンを全エンティティに拡張（Repository乱立のリスク）
   - 案B: Application層に UseCase / Query ハンドラを設けコントローラーを薄くする（`src/WISE.Application/UseCases`・`Queries` が既に存在、CQRS的分離との親和性）
   - 案C: 読み取り専用クエリは軽量な Query Service、書き込みは UseCase、という混成
   - **既存の `UseCases`/`Queries`/`Services` ディレクトリの使われ方を必ず確認**し、確立済みの流儀に寄せること。
3. **段階的計画**: 一度に全部変えず、①最も薄いコントローラー1〜2個でパターンを確立 → ②レビュー → ③残りに展開、の順で PR を分割する計画を提示。`WorksController` は最後に回す。
4. 承認後、第1弾（パターン確立分）のみ実装し、ビルド（`dotnet build`）と `src/WISE.Tests` のテストが通ることを確認。

### 成果物
- `Document/` 配下に是正方針ドキュメント（現状分析＋採用案＋根拠＋段階計画）
- 第1弾リファクタリングのコミット（テストパス確認済み）

---

## Prompt 2 — メタデータパイプラインの終了条件を「Provider特性ベース」に再設計

**種別**: ドメイン設計（Application層中心・中〜高難度）
**担当推奨**: Fable または Opus

### 背景
`src/WISE.Application/Services/MetadataService.cs` の `CollectResultsAsync` は Priority グループ単位の二段階収集（Phase1: テキスト早期終了、Phase2: カバーフォールバック）を行う。終了条件を決める `GetExitFields` が **MediaType と識別子プレフィックスのハードコード分岐**になっている：

```csharp
if (mediaType == MediaType.Comic) return ["Title", "Author", "Circle"];
if (mediaType == MediaType.Book)  return ["Title", "Author"];
if (identifier.StartsWith("FC2", ...)) return ["Title"];  // FC2は構造的にMaker/Actressを欠く
return ["Title", "Actress", "Maker"];
```

FC2の早期終了問題は個別パッチで塞いだが、これは対症療法。「あるProviderは特定フィールドを構造的に持たない」「あるProviderは低画質カバーしか返さない」といった **Provider特性** を、`MetadataService` 側の分岐でなく **Provider自身が宣言する**設計に寄せる余地がある。

### 依頼内容
1. `Document/Metadata.md` / `Pipeline.md` の設計思想を読み、現在の Tier/Priority/早期終了の意図を正確に把握する。
2. `IMetadataProvider`（`src/WISE.Domain/Interfaces/`）の現行契約（`Priority` / `CanHandle` / `SupportedMediaTypes` / `FetchAsync`）を確認し、**終了条件をProvider特性から導出する設計**を提案する。検討軸の例：
   - 「この識別子・MediaTypeで期待できるフィールド集合」をProvider（または軽量なポリシーオブジェクト）が申告できるか
   - カバー画質しきい値（現125KB、`FetchMetadataJobUseCase`側）とテキスト充足判定が別レイヤに散っている問題の整理
   - ハードコード分岐を消しても FC2 / Comic / Book の既存挙動を退行させないこと（**回帰防止が最優先**）
3. **過剰設計を避ける**こと。WISEは「引き算の美学」を掲げるプロダクト。プラグイン機構(`Document/Plugin.md`)の将来像と整合しつつ、今必要な最小の抽象に留める。
4. 提案が承認されたら実装し、代表品番（**FC2系・Comic・通常AV** 各1件以上）でスキャンログを確認して挙動が保たれることを検証する。

### 成果物
- 再設計方針メモ（現行のハードコード分岐一覧＋提案する抽象＋退行しないことの根拠）
- 実装コミット＋スキャンログによる回帰確認結果

---

## Prompt 3 — EF Core クエリ翻訳リスクの横断監査

**種別**: 品質監査（Infrastructure/Api横断・中難度）
**担当推奨**: Fable（機械的だが網羅性と正確な読解が要る）

### 背景
過去に Collections 機能で「複雑なLINQがSQLに変換できずランタイム例外/意図せぬクライアント評価」に相当する地雷を踏んだ実績がある（`d71fa5e` 等でEF Coreのエンティティ状態・マッピング周りを修正）。同種の問題が他コントローラーにも潜んでいないかを横断的に洗い出したい。特に `WiseDbContext` を直接使う11コントローラー（Prompt 1参照）はクエリが各所に散在しており温床になりやすい。

### 依頼内容
以下の EF Core（SQLite プロバイダ）翻訳リスクパターンを、`src/WISE.Api/Controllers/` 全体および `src/WISE.Infrastructure/Data/` を対象に静的監査する。**各指摘は必ず `ファイル名:行番号` で示すこと。**

監査観点：
1. **クライアント評価への意図せぬフォールバック** — `.Where()`/`.Select()` 内でC#のカスタムメソッド・正規表現・`string.Normalize()`・null条件演算子を使い、SQL翻訳不能で全件メモリ評価になっているもの。特に `DuplicatesController` の `NormalizeTitle`（`Regex.Replace` を含む）が LINQ ツリー内で呼ばれていないか。
2. **`GroupBy` 後の射影** — SQLite プロバイダで翻訳しきれない `GroupBy`+集計の組み合わせ。
3. **`.Contains()` on 大きなリスト** — `firstWorkIds.Contains(a.WorkId)` 型（`CollectionsController:44`に実例）が巨大リストで IN 句肥大化しないか。
4. **N+1** — ループ内 `await ...FirstOrDefaultAsync()`（`DuplicatesController.Resolve` の `DeleteWorkIds` ループ等）。
5. **`AsNoTracking` の付け忘れ/付けすぎ** — 読み取り専用なのにトラッキングしている、または更新対象に `AsNoTracking` が付いている。
6. **`ToList()`/`AsEnumerable()` の位置** — DBに落とすべきフィルタがメモリ側に漏れているもの。

### 成果物
- `ファイル名:行番号` 付きの指摘一覧（リスク度: 高/中/低で分類、各1〜2行の根拠）
- 明確かつ低リスクな修正はその場で修正しコミット。判断を要するもの（クエリ再構成が必要等）は一覧に「要相談」として残す。

---

## Prompt 4 — 設計ドキュメントと実装の乖離監査

**種別**: ドキュメント整合性監査（全体・高難度・深い読解要）
**担当推奨**: Opus（`Document/` 全16ファイル＋実装の突き合わせに読解力が要る）

### 背景
`Document/` 配下に16のMD（Architecture, Domain, Metadata, Pipeline, Database, API, UI, Identifier, RuleEngine, Plugin, Work, Roadmap, ImplementationPlan 等）と、最上位の「WISE v2 Product Constitution」（Metadata First / Work First / 引き算の美学）がある。実装がスプリントを重ねる中でドキュメントの思想から乖離していないか、逆にドキュメントが実装に追随できず陳腐化していないかを監査したい。

### 依頼内容
1. Constitution の3原則（**Metadata First / Work First / 引き算の美学**）を実装がどこで体現/違反しているか具体例を挙げる。例：機能追加でUIが「足し算」に傾いていないか、Work中心のデータモデルが崩れていないか。
2. 各設計ドキュメントについて、記述と実装の乖離を **「ドキュメントが古い」か「実装が逸脱」か** を判別して指摘する。特に重点：
   - `Metadata.md`/`Pipeline.md` の Tier/Priority 記述 vs 現行 `MetadataService`（Prompt 2 と関連）
   - `Domain.md` のエンティティ定義 vs `src/WISE.Domain/Entities/`（`Work`/`Asset`/`MetadataField` の実フィールド）
   - `API.md` のエンドポイント仕様 vs 実際のコントローラーのルート/レスポンス形状
   - `Database.md` のスキーマ vs 実マイグレーション
   - HANDOFFに記録された「フィールド名の大小文字不一致の罠」（`author`/`circle` 小文字 vs `Actress` 大文字）が `Domain.md` 等に明文化されているか
3. **監査に徹し、勝手に実装やドキュメントを書き換えないこと。** 乖離ごとに「ドキュメント側を直すべき／実装側を直すべき／設計判断が必要」の推奨アクションを付す。

### 成果物
- 乖離一覧レポート（`Document/` に出力）: 各項目に「該当ドキュメント箇所」「該当実装箇所（`ファイル:行`）」「乖離の種別」「推奨アクション」。
- 是正はユーザー承認後、別タスクとして切り出す（このセッションでは監査レポートまで）。

---

## 補足：追加で検討したい横断タスク候補

上記4件に加え、コードベース調査中に気づいた「Julesでは拾いにくい」候補。優先度はユーザー判断に委ねる。

- **`WorksController` の819行肥大化の解体** — Prompt 1 の最終段としても扱えるが、単体で「責務分割（検索/詳細/メタデータ編集/関連作品）」のリファクタとして切り出す価値あり。
- **動画再生カクつきの根本原因切り分け** — HANDOFF §5 に「複数回カクつき報告あり・根本原因未確定」とある。`VideoStreamCache`（先頭32MB LRU）＋ Range Request ＋ `PhysicalFile` の設計が、HDD実測 vs アプリ側バッファのどちらがボトルネックか、計測込みで診断するタスク。これは設計＋実測の両面判断が要るためモデル向き。
- **`DuplicatesController.Resolve` のトランザクション境界** — `DeleteWorkIds` ループ内で `SaveChangesAsync` を複数回呼んでおり、途中失敗時に部分適用が残るリスク。トランザクション設計の見直し（Prompt 3 の N+1 指摘とも関連）。
