# Performance Optimization Plan

Based on the architectural documents, here are the identified bottlenecks and suggested optimizations.

## Import Speed
**Bottleneck:** SQLite WAL mode and job queue lock contention during massive imports.
**Optimization:**
- Increase polling interval for Job Queue during high load.
- Batch insert operations for `METADATA_FIELD` and `ASSET` to reduce transaction overhead.
- Perform hash calculation asynchronously instead of during file discovery.

## Metadata Throughput
**Bottleneck:** EAV (Entity-Attribute-Value) pattern for `METADATA_FIELD` causes complex and slow compound queries. Event Log / Job Queue contention.
**Optimization:**
- Decouple metadata fetching from UI interactions.
- Cache frequently accessed metadata combinations.
- Aggregate domain events (e.g., `MetadataUpdated`) to reduce the number of events fired and processed.

## Gallery Loading
**Bottleneck:** Parsing and serving thumbnails/covers from ZIP/PDF archives or raw video frames directly on UI load.
**Optimization:**
- Implement `COVER_CACHE` (as proposed in Phase 2 docs) for both comic pages and video frame extractions.
- Lazy-load cover images using `loading="lazy"` attribute in HTML or Intersection Observer API.
- Use ETag and Cache-Control headers for cover image APIs (e.g., `/api/works/{id}/reader/cover`).

## Search
**Bottleneck:** `LIKE '%keyword%'` queries across large `METADATA_FIELD` datasets.
**Optimization:**
- Enable SQLite FTS5 (Full-Text Search) Virtual Table (`METADATA_FTS`) for `value` column.
- Use Contentless FTS5 tables to avoid duplicating storage, relying on the source table when needed.
- Implement asynchronous FTS index updates via `IndexUpdateJob`.

## Reader
**Bottleneck:** Decompressing ZIP/RAR files for every page request or rendering PDF pages on the fly, locking the main thread or consuming heavy memory.
**Optimization:**
- Introduce `ArchiveIndexJob` to build a page list (JSON cache) on import.
- Pre-fetch next 2-3 pages in the UI viewer into memory.
- For heavy formats like PDF, introduce a `.page-cache/` mechanism for rendered page tiles/images.
- Throttle (debounce) `READING_HISTORY` updates from the viewer (e.g., 5 seconds) and use `localStorage` for real-time state.

## Memory & CPU
**Bottleneck:** Instantiating archive readers (`IArchiveReader`) and loading entire files into memory.
**Optimization:**
- Use stream-based archive reading (e.g., `SharpCompress` streams) instead of loading the full archive.
- Ensure `IArchiveReader` implementations properly dispose of unmanaged resources.
- Rate-limit background jobs based on available CPU cores.

## SQLite
**Bottleneck:** Single-file database locking during concurrent read/write operations (e.g., jobs updating metadata while UI fetches gallery).
**Optimization:**
- Enable WAL (Write-Ahead Logging) mode and `PRAGMA synchronous = NORMAL`.
- Separate the background job queue (e.g., `JobLog`, `EventLog`) into a different physical SQLite file if I/O becomes a critical bottleneck.
- Implement exponential backoff for `database is locked` retries.

## Virtual Scrolling
**Bottleneck:** DOM node bloat and React render cycle delays with large galleries.
**Optimization:**
- Ensure `@tanstack/react-virtual` is implemented correctly to recycle DOM nodes.
- Maintain stable image dimensions to prevent layout shifts during virtual scrolling.

## Caching
**Bottleneck:** Regenerating heavy resources (covers, archive index, PDF pages).
**Optimization:**
- Introduce an application-level memory cache (e.g., LRU cache for 20 recently accessed pages).
- Permanent file-system cache for generated covers (`.thumbnails/` and `COVER_CACHE` DB tracking).
