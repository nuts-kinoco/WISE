# WISE Metadata Architecture Review

## 1. Evaluation of Existing Components

### 1.1 Metadata Schema & MetadataField Design
**Current State:**
- The schema uses an EAV (Entity-Attribute-Value) model via the `METADATA_FIELD` table.
- The `MetadataField` entity tracks `FieldName`, `Value`, `ProviderId`, `IsPrimary`, `ConfidenceScore`, and `FetchedAt`.
- SQLite FTS5 is integrated for cross-media text search on all primary fields.

**Strengths:**
- High flexibility to accommodate diverse media types (Video, Comic, Book) without altering the relational schema.
- Tracking provenance (`ProviderId`) and scoring per field enables robust conflict resolution.

**Weaknesses:**
- All values are stored as strings. JSON arrays (for tags) or integers (for page counts) require parsing upon read.

### 1.2 Provider Architecture
**Current State:**
- Abstracted via the `IMetadataProvider` interface.
- Orchestrated by `MetadataService` using a tiered execution strategy (Tier 1 for Priority >= 80, Tier 2+ for Priority < 80).
- Employs an early exit optimization: Tier 2 is skipped if Tier 1 successfully fetches critical fields.

**Strengths:**
- Asynchronous and parallel execution within tiers reduces total fetch time.
- Highly extensible for future plugins.

**Weaknesses:**
- The `MetadataService` hardcodes `Tier1ExitFields` (`Title`, `Actress`, `Maker`), which breaks the media-agnostic abstraction for Comic/Book pipelines where `Author` or `Circle` might be the essential fields.

### 1.3 Evidence System & Confidence Scoring
**Current State:**
- `IIdentifierResolver` aggregates signals from multiple `IEvidenceProvider` instances.
- Providers output `Evidence` with a `ConfidenceScore`.
- The resolver sums scores; if the total exceeds `IdentifiedThreshold` (60), it assigns the highest scoring value as the Identifier. Otherwise, it generates a deterministic UNKNOWN identifier.

**Strengths:**
- Pluggable evidence sources (filename, path, hashes) allow incremental improvements.
- Explicit scoring enables a transparent "Diagnostic View" for users to understand matching decisions.

**Weaknesses:**
- Simple score summation might overinflate confidence if multiple providers extract the same weak signal (e.g., path and filename yielding the same pattern).

### 1.4 Conflict Resolver
**Current State:**
- Implemented in `MetadataConflictResolver.cs`.
- Resolves conflicts per `FieldName` by sorting based on: 1. `Confidence`, 2. `Priority`, 3. `FetchedAt` (newest).
- `Work.ApplyResolvedMetadata` updates fields, ensuring at least one primary value exists per field.

**Strengths:**
- Completely deterministic and prioritizes higher confidence while falling back to source reliability.

### 1.5 Identifier Resolution
**Current State:**
- `IdentifierParser` isolates regex extraction logic (Commercial, Doujin, Date formats).
- Generates `IdentifierCandidate` objects without deciding their final validity.

**Strengths:**
- Excellent separation of concerns between raw extraction (`IdentifierParser`) and scoring (`FileNameEvidenceProvider`).

### 1.6 Metadata Normalization
**Current State:**
- Basic normalization is present (e.g., standardizing `FC2-12345` to `FC2-PPV-12345`, normalising FANZA prefixes).
- Documentation indicates `NORMALIZER_RULE` as a dynamic DB-driven rule engine.

### 1.7 Metadata Completeness & Quality
**Current State:**
- Defined conceptually via metrics: Fill Rate, Confidence, Freshness, and Cover Quality.

---

## 2. Can WISE Become the Strongest Metadata Database?
**Yes.** The combination of an EAV schema, source provenance (Provider/Confidence per field), deterministic conflict resolution, and the Evidence-based identification pipeline creates a highly resilient and adaptable system. By moving away from rigid, single-source-of-truth limitations, WISE is exceptionally positioned to act as a robust aggregator. To reach its full potential, it must address cross-media abstraction leaks (like hardcoded essential fields) and refine its scoring mechanics.

---

## 3. Suggestions

### 3.1 Missing Fields
- **Alt Titles (Aliases):** Essential for cross-language matching and varying naming conventions (e.g., `title_en`, `title_romaji`).
- **External IDs:** Explicit fields for common external databases (e.g., `tmdb_id`, `vndb_id`, `myanimelist_id`) to facilitate reliable cross-referencing.
- **Content Attributes:** Indicators such as `mosaic`, `uncensored`, or `content_warnings` (vital for commercial and doujin content).

### 3.2 Normalization Improvements
- **Pre-Parser Cleanup Pipeline:** Implement a robust string cleaner before regex parsing to strip common release group tags (e.g., `[1080p]`, `[WebRip]`) which often interfere with strict regex patterns.
- **Entity Normalization:** Implement fuzzy matching or alias tracking for Author/Circle/Maker names to prevent duplicating collections due to minor spelling or capitalization differences.

### 3.3 Future-Proof Schema
- **Typed Values in EAV:** Introduce a `DataType` column in `METADATA_FIELD` (e.g., `String`, `Number`, `Boolean`, `JsonArray`) to allow dynamic parsing without hardcoding logic in the application layer.
- **Language Tags:** Add a `Language` column or suffix directly to the EAV row (e.g., Field: `Title`, Lang: `ja-JP`) instead of creating separate field names like `title_en`.

### 3.4 Scalability
- **FTS5 Incremental Updates:** Ensure FTS5 updates are batched and executed asynchronously. A high volume of EAV updates can cause write-locks on SQLite if done synchronously.
- **Evidence Pruning:** Periodically prune low-confidence or rejected `Evidence` records from the database to prevent unbounded growth in large libraries.

### 3.5 Provider Priorities & Pipeline
- **Dynamic Tier 1 Criteria:** Refactor `MetadataService` so `Tier1ExitFields` are dynamically determined by `Work.MediaType` (e.g., `Author` for Books, `Circle` for Doujin) rather than being hardcoded for Video.
- **Weighted Evidence Summation:** Instead of simply summing Evidence scores, implement a cap or diminishing returns mechanism for similar evidence types to prevent score inflation.
