# WISE v2 - Adversarial UAT & Identifier Stress Test Report

## Overview
This is a QA report evaluating WISE v1.0 against the adversarial stress test requirements. I have verified the current behavior of the codebase by executing regex tests, inspecting the Domain logic (`IdentifierParser`, `IdentifierResolver`, `ImportUseCase`), and analyzing the `WatchFolderMonitorService`.

Based on my review, the system is **NOT ready for release (NO-GO)** due to several critical flaws in file identification, normalization, and merge behaviors.

---

## ① Critical Bug (Releaseできない問題)

**1. 実在するファイル名でのパース失敗とデータ汚染 (Normalizer未実装による副作用)**
- **Severity:** Critical
- **Steps to Reproduce:**
  Import `IPX-001_sample.mp4`, `IPX-001-C.mp4`, `IPX001.mp4`, `fc2_ppv_1234567.mp4`, `FC2 PPV 1234567.mp4`.
- **Expected:** These should be correctly identified as `IPX-001` and `FC2-PPV-1234567`.
- **Actual:** `IdentifierParser.cs` relies on strict word boundary `\b` regex without any pre-processing. As a result:
  - `fc2_ppv_1234567.mp4` fails to parse.
  - `FC2 PPV 1234567.mp4` fails to parse.
  - `IPX001.mp4` fails to parse.
  - `IPX-001_sample.mp4` fails to parse because `_` prevents `\b` from matching cleanly.
- **Possible Cause:** The system skipped implementing the `Normalizer` (as noted in `ExecuteImportJobUseCase.cs: // Normalizer は v1.0 未実装のため原文のまま`). Without normalization, real-world filenames completely break the identifier resolver.

**2. 誤検出によるIdentifier汚染 (RTX-4090, CAT-001)**
- **Severity:** Critical
- **Steps to Reproduce:**
  Import `Windows-11.mp4`, `RTX-4090.mp4`, `CAT-001.jpg`, `ABC-12.pdf`.
- **Expected:** Should be treated as `UNKNOWN`.
- **Actual:** The `CommercialRegex` in `IdentifierParser.cs` is `\b([A-Z]{2,6})-(\d{2,})\b` and a comment explicitly says `// ホワイトリスト方式は廃止。任意のプレフィックスを許容する。`. This matches `RTX-4090` (RTX-4090), `CAT-001` (CAT-001), and `ABC-12` (ABC-12), treating these generic files as valid commercial AVs and polluting the database.
- **Possible Cause:** The removal of the whitelist allows any A-Z string with a hyphen and numbers to match, which is way too loose for a media library that may contain diverse files.

**3. UNKNOWNファイルのハッシュ衝突と誤マージ**
- **Severity:** Critical
- **Steps to Reproduce:**
  Import multiple unrelated files that happen to share a name (e.g. `movie.mp4`, `sample.mp4`, `IMG0001.mp4`) and have the same size (e.g. empty test files of 0 bytes, or same sized template files).
- **Expected:** Each file gets a unique `UNKNOWN` ID or is treated as an isolated Work.
- **Actual:** `IdentifierResolver.cs` uses `SHA256(FileName + FileSize)` to generate a deterministic ID. Unrelated files with the same name and size are assigned the exact same `UNKNOWN-XXXX` identifier and merged into the same Work!
- **Possible Cause:** Over-reliance on deterministic generation instead of generating true UUIDs for orphaned/unknown files.

---

## ② High (v1.0で直すべき問題)

**1. Watch Folder の競合エラー (不完全ファイルのインポート)**
- **Severity:** High
- **Steps to Reproduce:**
  Copy a large file slowly to the Watch Folder.
- **Expected:** Processed only after the copy completes.
- **Actual:** `WatchFolderMonitorService` uses `FileSystemWatcher` and checks file lock via `File.Open`. However, it triggers processing if the file lock check passes after a mere 3-second stabilization period, which can easily fail or cause partial processing for large network transfers or intermittent copy processes.
- **Possible Cause:** `IsFileStabilized` logic is brittle for large media files being copied over network shares.

**2. 大量インポート時のメモリ枯渇とブロック**
- **Severity:** High
- **Steps to Reproduce:**
  Analyze a directory with 10,000 files in the UI.
- **Expected:** Fast preview, no UI freezing.
- **Actual:** `ImportUseCase.AnalyzeDirectoryAsync` loads all files, instantiates `Asset` entities, and resolves identifiers for every single file simultaneously before returning the HTTP response. For 10,000 files, this blocks the API thread and causes significant memory usage, potentially causing timeouts in the UI.
- **Possible Cause:** No pagination or background streaming for the Analyze phase.

---

## ③ Medium (v1.1でもよい問題)

**1. 同一ファイル名・別拡張子のDuplicate Merge挙動**
- **Severity:** Medium
- **Steps to Reproduce:**
  Import `IPX-001.mp4`, `IPX-001-4K.mp4`, `IPX-001-CD2.mp4`.
- **Expected:** Work 1, Asset 3.
- **Actual:** They successfully merge into 1 Work because `CommercialRegex` matches `IPX-001`. However, the UI/DB does not distinctly track whether an asset is a "part" (CD2) or a "variant" (4K) vs a pure duplicate. They are just dumped into `work.Assets`.
- **Possible Cause:** Asset metadata extraction lacks part/variant handling.

---

## ④ Nice to Have (改善案)

1. **Parallelization in Metadata Pipeline:** `FetchMetadataJobUseCase` runs providers sequentially. Parallelizing these calls would vastly improve job processing times.
2. **Debounce UI clicks:** The frontend should strictly disable buttons during API calls to prevent concurrent `ExecuteImportJobUseCase` runs which could cause SQLite `database is locked` errors due to concurrent `SaveChanges`.

---

## ⑤ Architecture Review (アーキテクチャ不一致)

1. **Normalizerの欠如 (Identifier.md違反):** `Identifier.md` Section 2 explicitly mandates a `Normalizer` to handle noise removal and formatting. The implementation explicitly skips it (`v1.0 未実装`), directly violating the core design and failing the FC2/chaos tests.
2. **UNKNOWNの仕様 (Work.md違反):** `Identifier.md` and `Work.md` specify that Unknown files should be "Orphaned Assets" needing user confirmation. Instead, the code fabricates a pseudo-identifier (`UNKNOWN-XXXX`) and creates dummy Works for them, leading to the collision bugs mentioned above.
3. **Whitelist廃止 (RuleEngine.md違反):** The removal of the commercial AV whitelist makes the regex too greedy, identifying random PDFs or images as Works, violating the goal of accurate library curation.

---

## 【結論】Release判定: ❌ NO-GO (リリース不可)

本システムは、実在するファイル名（`FC2 PPV 1234567.mp4`, `IPX-001_sample.mp4`など）の識別機能が欠落しており、かつ `CAT-001.jpg` などの誤検出が大量発生します。さらに、同名異ファイルの誤マージ（`movie.mp4`）という**致命的なデータ破壊の危険性**があります。

QAエンジニアの視点として、これらの「データ汚染・データ破壊」に関するCritical Bugを修正するまで、v1.0としてのリリースは承認できません。
