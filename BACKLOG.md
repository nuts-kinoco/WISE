# WISE v2 残課題バックログ

## Step 1 (Gallery) — 残課題

- [ ] API サーバー起動状態での実作品表示・Virtual Scroll 動作確認
- [ ] WorkCard Rich モードで rating 非表示時の下部余白（estimateSize 固定値による）
- [ ] 10,000件規模でのパフォーマンス実測（Virtual Scroll は実装済み）

---

## Step 2 (Detail Page) — 残課題

- [ ] API 起動時の実作品での isComplete バッジ・History タイムライン動作確認
- [ ] サンプル画像フィルムストリップ: 縦長画像 (aspect-ratio 2:3) のサムネイル高さ調整検討
- [ ] Related Works セクション（APIサポート待ち）

---

## Step 3 (Video Player) — 残課題

- [ ] API 起動時の実動画での再生位置記憶動作確認（localStorage `wise-video-pos-{assetId}`）
- [ ] シーク中のサムネイルプレビュー（canvas ベース、将来対応）
- [ ] キーボードショートカット（スペース/矢印キー）— ブラウザ標準 `controls` で現状カバー済み

---

## Step 4 (Organize) — 残課題

- [ ] API 起動時の実データでの Progressive Fetch 速度・UX 確認
- [ ] 10,000件規模での Virtual Scroll フレームレート実測
- [ ] `handleRatingChange` / `handleFavoriteChange` の `pendingChanges` クロージャ問題: undo の oldValue が stale になる可能性あり（要動作確認）

---

_各 Step 完了時にここへ追記する_
