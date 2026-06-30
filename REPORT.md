# WISE v2 QA UAT Report

## Executive Summary
Assessment: **NO-GO**
The backend core systems (Database entity state tracking, Job Queue, MediaType detection, Asset properties, and Metadata pipelines) contain critical gaps that prevent the application from functioning at a basic library level. The system successfully imports files and assigns identifiers but fails completely at job processing, type classification, and metadata enrichment.

## Failures & Findings

### 1. FAIL: EF Core Entity State Tracking Bug (Critical)
**Steps to Reproduce:**
1. Call `/api/Jobs/import` pointing to a folder with valid media files.
2. BackgroundJobWorker picks up the `Import` job and begins processing.
**Expected:** The job finishes successfully and saves entities into SQLite.
**Actual:** Job fails with `DbUpdateConcurrencyException`. "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)".
**Cause:** The EF Core DbContext (`WiseDbContext.ApplyEntityStateFixesAsync`) was checking the state for `Asset` and `MetadataField`, but neglected `Work` entities created with client-side GUIDs, causing EF to issue an UPDATE for a non-existent Work rather than an INSERT.
**Suggested Fix:** Add `e.Entity is Work` into the `ApplyEntityStateFixesAsync` override to force `EntityState.Added` for missing Works.

### 2. FAIL: FetchMetadata Jobs Never Queue (Critical)
**Steps to Reproduce:**
1. Complete an Import job with `"UseMetadataPipeline": true`.
2. Wait for BackgroundJobWorker to process the metadata queue.
**Expected:** `FetchMetadata` jobs run, fetch data from external providers, and update `MetadataFields` in SQLite.
**Actual:** All 919 `FetchMetadata` jobs are permanently stuck in `JobStatus.Created` (Status = 0). None are ever picked up.
**Cause:** In `ExecuteImportJobUseCase.cs`, the code creates `var metadataJob = new Job("FetchMetadata", ...)` and adds it to the DB, but fails to call `metadataJob.MarkAsQueued()`. The background worker only polls `Status == JobStatus.Queued` (Status = 1).
**Suggested Fix:** Call `metadataJob.MarkAsQueued()` before saving to `_dbContext.Jobs`.

### 3. FAIL: AssetRole and StorageFormat Always Unknown/SingleFile (High)
**Steps to Reproduce:**
1. Import a mix of .mp4, .zip, .pdf, and .jpg files.
2. Query `SELECT role, storageformat FROM assets;` in SQLite.
**Expected:** Role reflects Video/Archive/Image, StorageFormat reflects Archive/SingleFile/Pdf.
**Actual:** 100% of Assets (927/927) have `AssetRole = 0` (Unknown) and `StorageFormat = 0` (SingleFile).
**Cause:** `ExecuteImportJobUseCase.cs` creates assets via `new Asset(finalFilePath, fileName, fileInfo.Length, "sha256-pending");` but completely lacks any logic, regex, or extension-check mechanism to infer and assign `Role` and `StorageFormat`.
**Suggested Fix:** Implement an `IAssetTypeResolver` to inspect extensions and headers and correctly assign `Role` and `StorageFormat`.

### 4. FAIL: MediaType Always Video (High)
**Steps to Reproduce:**
1. Import a zip file or pdf file.
2. Query `SELECT mediatype FROM works;` in SQLite.
**Expected:** .zip yields Comic/ImageCollection, .pdf yields Book.
**Actual:** 100% of Works (919/919) have `MediaType = 1` (Video).
**Cause:** The `Work` constructor defaults to `MediaType.Video`. The `ExecuteImportJobUseCase` creates Works via `work = new Work(identifier);` but never calls `work.SetMediaType(...)`.
**Suggested Fix:** Resolve MediaType dynamically from the primary Asset's extension/type and update the Work prior to saving.

### 5. FAIL: Missing Provider Interfaces (Medium)
**Steps to Reproduce:**
1. Compile the project with Windows Targeting (`dotnet build`).
**Expected:** Clean compilation.
**Actual:** Multiple compile errors. Providers returned `Task<IEnumerable<MetadataCandidate>>` but `IMetadataProvider` requested `Task<MetadataResult>`. Missing definitions for `ProviderDiagnostic`, `AppSetting`, `ICookieProvider`, and `FailureReason`.
**Cause:** Mismatched implementations between architecture documents and actual coded interfaces inside `WISE.Domain` and `WISE.Infrastructure`.
**Suggested Fix:** Align interfaces to use `MetadataResult` consistently, and ensure all domain entities outlined in the docs are present in the code.

## Evidence Checklist
- [x] Background worker logs extracted showing Import/Job queue failures.
- [x] SQLite queries confirming 0 MetadataFields, 0 CoverCaches, 100% Unknown AssetRoles.
- [x] API responses confirming jobs getting stuck in Queue/Created states.
