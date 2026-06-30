# WISE Phase 3 — UX Enhancement Roadmap

## 方針
新機能追加ではなく「磨くこと」。既存DBのみ利用。AIなし。新MediaTypeなし。
目標: 「毎日開きたくなるメディアライブラリ」

---

## Sprint 23 — Sort（サーバーサイドソート）
**Status: IN PROGRESS**

density切替（compact/normal/rich/list）は Sprint 14-22 で実装済み。
今 Sprint の核心は API / Store / UI への sort パラメータ追加。

### 実装
- `GET /api/works?sort=added|rating|title|identifier|release|random`
- `useGalleryStore`: `sort` / `setSort` 追加（localStorage 永続化）
- `useWorks`: sort を queryKey と fetchWorks に追加
- Gallery ヘッダー: MediaType タブ行の右端にソートセレクタ追加

### 完了条件
- ソート切替で即座に並び替わる
- MediaType フィルター × sort の組み合わせが正常動作
- リロード後も sort 設定が保持される

---

## Sprint 24 — Dashboard Home
**Status: PENDING**

### 実装
- `GET /api/home?deviceId=` — 1リクエストで4データセット返却
- `/home` 新設（または `/` をタブ構成に変更）
- ウィジェット: Continue Watching/Reading / Recently Added / Favorites / Random Pick
- 0件ウィジェットは非表示（引き算）

### 完了条件
- 途中で止めた作品が Continue に表示される
- インポート直後に Recently Added に表示される
- Random Pick でランダム遷移

---

## Sprint 25 — UX ポリッシュ（Empty / Loading / Error）
**Status: PENDING**

### 実装
- Empty State: Library Empty / Search Empty / Collection Empty
- Skeleton: WorkCard / Detail ページ
- Error メッセージ: JSON・例外クラス名を人間向けに変換
- プログレス表示: Import中 / Metadata取得中

### 完了条件
- 全対象画面でデータ 0件時に意味ある UI が出る
- エラー時に JSON が表示されない

---

## Sprint 26 — Related Works + Timeline 再設計
**Status: PENDING**

### 実装
- `GET /api/works/{id}/related?field=actress&limit=8`
- Detail ページ下部に RelatedWorks 横スクロール追加
- `/history` を日付グループ + 日本語テキストに再設計（JSON禁止）

### 完了条件
- 同女優/サークルの作品が Detail に表示される
- History が日本語で読める

---

## Sprint 27 — Collection
**Status: PENDING**

### 実装（唯一の DB 変更 Sprint）
- `Collection` / `CollectionItem` Entity 追加
- Migration 1本
- CRUD API + `/collections` UI ページ
- WorkCard にコンテキストメニュー「コレクションに追加」

### 完了条件
- コレクション作成 → 作品追加 → Gallery 表示 → 削除 が動作

---

## Phase 4 以降（持ち越し）
- Group 表示（MediaType / Actress / Series 単位）— VirtualScroll+グループヘッダーのコスト大
- Advanced Search（フィルターチップ: ジャンル / 評価範囲 / 日付範囲）
- AI 機能（禁止 / Phase 5）
