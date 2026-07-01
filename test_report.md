# WISE GO/NO-GO Report
**Date:** $(date)
**Verdict:** CONDITIONAL GO

## CRITICAL Issues (auto NO-GO)
- **Potential OOM Risk on Comic Reader**: The `ReaderController` caches archive pages in memory, and the DI setup (`builder.Services.AddMemoryCache()`) in `Program.cs:25` specifies no global `SizeLimit`. Unbounded caching of large byte arrays from multiple concurrent sessions could crash the server.

## HIGH Issues
- **Archive Page Stress Test Causes Freezes / Timeouts**: Running concurrent reads via curl for Archive pages locked up or severely delayed the server, causing test curl requests to timeout (taking over 400 seconds). This implies an unbounded blocking operation, an N+1 scaling issue within archive traversal, or the aforementioned memory bloat causing GC pauses.
- **Corrupted Archives Return Unhandled 500 HTML/JSON (System.IO.InvalidDataException)**: When requesting pages for a known corrupted ZIP (`/tmp/wise_qa_corpus/archives/corrupt.zip`), the backend propagates a `System.IO.InvalidDataException: Central Directory corrupt` directly to the client as an HTTP 500 error instead of failing gracefully with a 400 or 422 status response.

## MEDIUM Issues
- **MediaType / StorageFormat Mismatches**: A PDF named `EKDV-775-bonus.pdf` got correctly identified as `EKDV-775` (MediaType Book). However, when `EKDV-775.mp4` imported, it joined the same Work. Thus, a Video format is under a `Book` MediaType. Identifier collisions where multiple format types share the same exact identifier prefix will cause them to merge improperly, leading to unplayable or incorrectly categorized works.
- **UNKNOWN Work Sprawl for Simple Archives**: Archives like `single_page.zip` and `corrupt.zip` failed to use the filename stem for identification because they lacked a matching pattern, defaulting to `UNKNOWN-` identifiers instead.

## LOW / CONCERN
- **Absolute Paths Stored in DB**: Assets are saved via `FilePath` using absolute paths (e.g. `/tmp/wise_qa_corpus/videos/FC2-4841573.mp4`). If the library directory is moved, all paths break.
- **No File Size Constraint for Dummy Covers**: Although `MinCoverFileSizeBytes = 8_000` applies for *downloaded* metadata covers, small dummy files added directly from local storage like `cover.jpg` (1KB) still process through.

## PHILOSOPHY VIOLATIONS (Constitution failures)
- None. Deduplication logic correctly enforces "DB as Source of Truth" and "Work First" axioms during imports, and the application avoids sending back full assets inside metadata arrays.

## FIXED (verified regressions still fixed)
- **FC2 Normalization:** `FC2-4841573.mp4` successfully normalizes into `FC2-PPV-4841573`, joining its counterpart correctly.
- **EKDV Underscore Normalization:** `EKDV_775`, `EKDV775`, and `EKDV-775-C` are all correctly merged under the `EKDV-775` PrimaryIdentifier.
- **Local Cover Assignments:** Specific image patterns like `cover.jpg`, `folder.jpg`, `front.jpg`, `_pl.jpg`, and `_ps.jpg` are accurately tagged with the `CoverPortrait` or `CoverLandscape` roles. Non-cover images like `DSC_0001.jpg` are tagged properly as generic images.
- **Noise / Artifact File Skips:** `sample.mp4`, `movie.mp4`, `Thumbs.db`, `.DS_Store`, etc., are appropriately blocked and no longer generate `UNKNOWN` works.

## WONT FIX (acknowledged, deferred)
- `IPX999.mp4` (without delimiters) fails to normalize. This is acknowledged due to the 4-letter minimum constraint in the commercial parser regex.

## Refactoring / Improvement Proposals
- `src/WISE.Api/Program.cs:25`: Pass options to `AddMemoryCache(options => options.SizeLimit = <safe_limit>)` to prevent OOM.
- `src/WISE.Infrastructure/Archive/ZipArchiveReader.cs:25`: Add a `try/catch` block for `InvalidDataException` when attempting to read the central directory, and return a clean error object indicating the file is corrupted.

## Verdict Rationale
The core features implemented recently (regex normalization, noise exclusion, cover detection, and generic book format import) work remarkably well. However, the system is conditionally blocked from a full "GO" due to a structural weakness in the Comic Reader's unbounded memory cache, which allows a trivial concurrent workload or corrupted archive to DoS or crash the service. Once `AddMemoryCache` bounds are constrained and corrupt archive handling catches `InvalidDataException`, it is cleared for release.
