# WISE v2 — Real World Filename Corpus Regression Test
# Jules QA Prompt v2.0（Claude実装者レビュー済み）

あなたは実装者ではありません。
あなたは Release QA Engineer です。

コードを修正してはいけません。
リファクタリングしてはいけません。
修正案は書いても構いませんが、**コード変更は禁止**です。

目的は「実在しそうなライブラリを構築した場合にWISEが壊れる箇所を見つけること」です。

PASSを探すのではありません。FAILを探してください。

---

# Step1: TestCorpus生成

PowerShell等で実際に存在しそうなフォルダ構成を生成してください。
空ファイルではなく、可能なら以下を含めてください。

- ダミー画像（1x1px JPEG/PNG）
- ダミーZIP（内部にJPEGを数枚格納）
- ダミーMP4（FFmpegで1秒動画を生成、または0バイトmp4）
- ダミーCBZ / RAR / PDF

## Commercial AV（最低500件）

命名ゆらぎを網羅すること。

```
EKDV-775.mp4
EKDV775.mp4
EKDV_775.mp4
EKDV-775-C.mp4
EKDV-775-CD2.mp4
EKDV-775-4K.mp4
hhd800.com@EKDV-775.mp4
www.javdb.com@EKDV-775.mp4
【無修正】SONE-001.mp4
SONE001_1080p.mp4
[PRED-999] タイトル.mp4
PRED-999 (1).mp4
PRED-999 copy.mp4
```

フォルダ形式も含める。

```
EKDV-775/
  EKDV-775.mp4
  cover.jpg             ← _pl.jpg でも _ps.jpg でもない
  cover_l.jpg
  sample01.jpg
  sample02.jpg
  rpin-010_pl.jpg       ← 別IDのカバーが意図的に混入
```

## FC2（最低300件）

```
FC2-PPV-4841573.mp4
FC2PPV4841573.mp4
FC2 PPV 4841573.mp4
fc2_ppv_4841573.mp4
FC2-4841573.mp4         ← FC2-PPV-に正規化されるか（要確認）
FC2-PPV-4841573/
  cover.jpg
  sample01.jpg
  movie.mp4
FC2-PPV-9999999.mp4     ← 実在しないID（スクレイパーが404を返すはず）
```

## DLSite（最低500件）

```
(RJ123456) タイトル.zip
(RJ123456) [Circle] タイトル.zip
RJ123456.cbz
RJ123456.rar
RJ123456.pdf
RJ01636799.zip                                          ← 8桁ID（新形式）
RJ01636799 [DL版].zip
(RJ01636799) [みくろぺえじ] 性悪ギャル躾けられる.zip
```

## Comic Market（最低500件）

```
(C100)[50on!]濁音2.zip
(C101)[Circle]Title.cbz
(C102)[サークル名]総集編.rar
(C103)[Circle (Author)] タイトル (ジャンル).zip
(コミティア156)[みくろぺえじ (黒本君)] 性悪ギャル躾けられる (コミティア).zip
(同人誌)[Iris art (戸田比佐也)] 敵に捕まった女戦闘員.zip
(同人誌)[武田弘光] アートワークス.zip
(同人誌)[作者名] タイトル [RJ01234567].zip              ← RJと同人フォーマット混在
```

## 成年コミック（最低500件）

```
[作者名] タイトル.zip
[作者名] タイトル【DL版】.zip
作者名 - タイトル.rar
タイトル 第01巻.cbz
タイトル Complete.zip
[作者名] タイトル vol.01-05.zip
[作者名（読み仮名）] タイトル.zip
```

## 商業コミック（最低300件）

```
キングダム 第01巻.zip
キングダム 01.cbz
【デジタル版】キングダム.pdf
キングダム_vol01.cbz
Manga_Title_v001.cbz    ← 英語タイトル
```

## EPUB（最低100件）

```
タイトル.epub
Author - Title.epub
[Publisher] Title.epub
タイトル 第01巻.epub
```

## PDF（最低100件）

```
タイトル.pdf
scan_001.pdf            ← スキャンPDF（ページ数不定）
document.pdf
タイトル【DL版】.pdf
```

## Image Folder（最低300件）

```
Comic001/
  001.jpg
  002.jpg
  003.jpg
  cover.jpg
ImageSet_NoExtension/
  001                   ← 拡張子なし
  002
ComicWithMixedFiles/
  001.jpg
  002.png               ← JPGとPNG混在
  003.webp
  Thumbs.db             ← ゴミファイル混在
  desktop.ini
SingleImage/
  image.jpg             ← 1枚しかない画像フォルダ
```

## Mixed Folder（最低300件）

```
混在フォルダ/
  動画.mp4
  comic.zip
  document.pdf
  image.jpg
  README.txt
  movie.nfo
  thumbnail.jpg
  Thumbs.db
```

## Garbage（最低500件）

```
README.txt
Windows11.iso
RTX4090.pdf             ← PDFだがゴミ
movie.mp4               ← 識別子不明
sample.mp4              ← 「sample」という名前
abcdefg.mp4             ← ランダム文字列
Thumbs.db
desktop.ini
.DS_Store
~$document.docx         ← ロックファイル
```

---

# Step2: Import実行

生成したTestCorpusをWISEへImportしてください。

- ImportMode: Copy
- UseMetadataPipeline: true
- エンドポイント: POST /api/jobs/import

---

# Step3: Metadata取得を最後まで待つ

全ジョブが Complete または Failed になるまで待機してください。
ステータス確認: GET /api/jobs

**重要**: Queued / Processing のまま固まっているジョブがあれば、それ自体をFAILとして報告してください。

---

# Step4: 以下を確認してください

## ■ Identifier（識別子抽出）

確認項目。

- 抽出成功率（identifier が null でない割合）
- Unknown率（空文字・"Unknown" になった件数）
- FC2-4841573 が FC2-PPV-4841573 に正規化されたか
- EKDV-775 と EKDV775 が同一Workにまとまったか、別Workになったか
- ゴミファイル（movie.mp4, sample.mp4）に誤って識別子が付与されたか
- FC2-PPV-1234567 と FC2-1234567 が別Workに分裂していないか

確認コマンド例（SQLite直接）。

```sql
SELECT PrimaryIdentifier, count(*) as cnt FROM Works GROUP BY PrimaryIdentifier HAVING cnt > 1;
SELECT count(*) FROM Works WHERE PrimaryIdentifier IS NULL OR PrimaryIdentifier = '';
```

## ■ MediaType

期待する分類。

| ファイル種別 | 期待MediaType |
|---|---|
| .mp4 / .mkv / .avi | Video |
| .zip / .cbz / .rar（同人・コミック） | Comic |
| .epub | Book |
| .pdf | Book または Comic（実装依存） |
| ImageFolder | ImageCollection または Comic |

確認すべきFAIL。

- ゴミPDF（RTX4090.pdf）が Book として取り込まれていないか
- 同人ZIPが Video に誤分類されていないか
- 動画+ZIPが混在したフォルダで、MediaTypeが正しいか

## ■ Asset

AssetRole が期待通りか。

| ファイル | 期待 AssetRole |
|---|---|
| _pl.jpg | CoverLandscape |
| _ps.jpg | CoverPortrait |
| .thumbnails/thumbnail.jpg | Thumbnail |
| cover.jpg（フォルダルート） | 不定（要確認） |
| .mp4 | Video |
| .zip / .cbz | Archive |

実装者注意点（過去に問題が発生した箇所）。

- `.thumbnails/` 内のファイルが孤立アセットとして誤削除されないか
- 同一FilePath の Asset が複数DBに存在しないか（重複アセット問題）
- 別ID（EKDV-775）のカバー画像が別Workのフォルダに混在していたとき、誤って別WorkのAssetに関連付けられないか
- AssetType=Thumbnail が複数存在した場合、最新1件だけ残るか（Thumbnail重複クリーンアップの動作確認）

## ■ Duplicate（重複・統合）

確認項目。

- EKDV-775.mp4 と EKDV775.mp4 → 同一Workにまとまったか、別Workになったか（設計上どちらが正しいか）
- FC2-PPV-4841573/ フォルダ内の movie.mp4 と、単体の FC2-PPV-4841573.mp4 → 同一Workにまとまったか
- 別IDのファイルが誤Mergeされていないか（異なる識別子が1つのWorkになっていないか）

## ■ Cover

優先順位が正しく適用されているか。

1. MetadataからダウンロードされたPortraitCover（最優先）
2. _pl.jpg / _ps.jpg（ローカルカバーファイル）
3. ArchiveCoverProvider（ZIPの1ページ目）
4. FFmpegサムネイル（動画のみ）
5. DefaultCover（プレースホルダー）

確認すべきFAIL。

- cover.jpg（_pl / _ps でない）が CoverLandscape / CoverPortrait として認識されているか
- PortraitCoverが取れているのにホームにFFmpegサムネイルが表示されていないか
- カバーURLが /api/assets/{id}/content でなく生のファイルパスになっていないか
- 小さすぎるカバー（8KB未満）が採用されていないか
- 存在しないassetIDのカバーURLが返ってきていないか（→ブラウザで404になるか確認）
- LandscapeCoverとPortraitCoverが同一assetIDを指している場合、詳細ページで両方表示されるか

## ■ Comic Reader

ZIPアーカイブ。

- 1ページしかないZIP → 読めるか
- 10000ページのZIP → メモリ問題が起きないか
- ページがJPGとPNG混在 → すべて表示されるか
- 拡張子なし画像ファイルをZIPに含める → スキップされるか、それとも壊れるか
- ネスト構造ZIP（zip内zip）→ 内部ZIPをページとして表示しないか
- RAR / CBR → 読めるか（libunrarの有無を確認）
- パスワード付きZIP → エラーが適切に返るか（500でなく400 / 422）
- 破損ZIP（ゴミバイトを末尾に追記したもの）→ 500で落ちないか
- ページ数0のアーカイブ → /api/reader/{id}/pages が500にならないか

PDF。

- PDFがReaderで開けるか
- PDFのページ数が正しく取得されるか

## ■ Gallery（一覧表示）

確認項目。

- タイトルが null の場合、ライブラリ / ホームで表示崩れが起きないか
- カバーが null の場合、プレースホルダーが表示されるか
- 5000件表示でVirtualScrollが固まらないか
- 長いタイトル（200文字以上）でUIが崩れないか
- 日本語タイトルのソート順が崩れていないか

## ■ Detail（詳細ページ）

確認項目。

- Metadata欄：全フィールドがnullでも崩れないか
- Assets欄：AssetType=Unknown のファイルが表示されるか
- History欄：ReadingHistoryが空でも表示されるか
- Reader / Playerボタン：アーカイブ系のみ表示されるか、動画は再生ボタンか

実装者注意点（このセッションで実際に問題が発生した箇所）。

- CoverUrl と CoverLandscapeUrl が同一 assetID になっている場合、詳細ページで両方表示されるか
- ArchiveCoverProvider が /api/works/{id}/cover を返すとき、詳細ページがそのURLを正しく利用しているか
- /api/works/{id} レスポンスに coverUrl と coverLandscapeUrl が必ず含まれるか（null でも）

## ■ DB整合性

確認SQLクエリ（SQLite直接実行）。

```sql
-- 孤立アセット（WorkIdが存在しないAsset）
SELECT count(*) FROM Assets WHERE WorkId NOT IN (SELECT Id FROM Works);

-- FilePath重複アセット
SELECT FilePath, count(*) as cnt FROM Assets
WHERE FilePath IS NOT NULL
GROUP BY FilePath HAVING cnt > 1;

-- MetadataField孤立
SELECT count(*) FROM MetadataFields WHERE WorkId NOT IN (SELECT Id FROM Works);

-- Thumbnail重複（同WorkIdで複数Thumbnail）
SELECT WorkId, count(*) as cnt FROM Assets
WHERE AssetType = 4
GROUP BY WorkId HAVING cnt > 1;

-- Assetが1件もないWork
SELECT count(*) FROM Works WHERE Id NOT IN (SELECT WorkId FROM Assets);

-- FilePath = NULL のAsset（ダウンロード失敗などで中途半端に登録されたもの）
SELECT count(*) FROM Assets WHERE FilePath IS NULL OR FilePath = '';
```

## ■ API安定性

以下を全件で確認してください。

```
GET  /api/works?limit=9999           → 正常にJSONが返るか（OOMで500にならないか）
GET  /api/works/{存在しないID}        → 500ではなく404を返すか
GET  /api/works/{id}                 → coverUrl が /api/assets/{id}/content または null か
GET  /api/assets/{存在しないID}/content → 500ではなく404を返すか
GET  /api/reader/{id}/pages          → ページ数0のアーカイブで500が出ないか
POST /api/jobs/fetchmetadata         → 存在しないWorkId → 400か404か（500ではないか）
GET  /api/works/{id}/cover           → アセットがない場合に500が出ないか
```

null・500・タイムアウト・壊れたJSON があれば全てFAILとして報告。

## ■ Metadata Pipeline

各プロバイダーのFAIL確認。

- **DLSiteMetadataProvider**: RJ番号で取得できるか。LandscapeCover / PortraitCover フィールドが正しく設定されているか（cover_url という誤フィールド名で来ていないか）
- **FanzaMetadataProvider**: d_XXXXXX（同人）識別子で dc/doujin エンドポイントを叩いているか。通常AV識別子（EKDV-775）は digital/videoa を使っているか
- **DoujinishiFilenameMetadataProvider**: `(イベント) [サークル (著者)] タイトル (ジャンル).zip` を正しくパースするか。著者名が省略されている場合にクラッシュしないか
- **MgsMetadataProvider**: mgsCookies.txt がなくても adc=1 だけで年齢確認ページを突破できるか（できないのが正常）。mgsCookies.txt を設定した場合にタイトルが取得できるか
- **ConflictResolver**: 同じフィールドに複数候補がある場合、Priority・Confidence順で正しく選択されているか。同優先度の場合にランダムになっていないか

## ■ Performance

- 1000件Import → 完了まで何分かかるか（目安：5分以内）
- GET /api/works?limit=1000 → レスポンスタイム（目安：3秒以内）
- 50件同時FetchMetadataジョブ → デッドロック / EF Core ObjectDisposedException が起きないか
- ArchiveCoverProvider で 100MB のZIPを開く → タイムアウト / OOM が起きないか
- SQLite WALモード未設定の場合、並列書き込みでロックエラーが出ないか

---

# Step5: FAILのみ報告してください

PASSはいりません。

以下の形式でまとめてください。

```
----------------------
Issue:         （タイトル）
Severity:      Critical / High / Medium / Low
Reproduction:  （再現手順）
Expected:      （期待動作）
Actual:        （実際の動作）
Root Cause:    （推測される原因）
Evidence:      （ログ / SQLクエリ結果 / スクリーンショット）
Suggested Fix: （修正方針のみ、コードは書かない）
----------------------
```

Critical / High / Medium / Low に分類すること。

---

最後に **Top 10 Critical Issues** だけまとめてください。

コード修正は禁止です。証拠だけ提出してください。
