# WISE Metadata Collection Architecture Evaluation

## 1. Executive Summary
This report evaluates the current metadata collection architecture of WISE to determine its potential to become a world-class metadata collector. The current architecture demonstrates a solid foundation with priority-based conflict resolution, strategy patterns, and fallback mechanisms. However, several critical gaps must be addressed to achieve world-class status, notably in retry policies, robust rate-limit handling, persistent session management, and comprehensive provider coverage.

## 2. Architecture Review

### Provider Architecture
**Current State:** The architecture uses a solid `IMetadataProvider` interface, allowing decoupled implementations. Providers are orchestrated by `MetadataService` and prioritize their outputs via `MetadataConflictResolver`. A dual-tier execution strategy (Tier 1 primary sources vs. Tier 2 fallback sources) with early exit is an excellent design pattern.
**Evaluation:** Good. The foundation is scalable and supports adding new providers easily.
**Recommendation:** Enforce `SupportedMediaTypes` checking rigorously as described in `Metadata.md` to prevent unnecessary API calls (e.g., stopping FANZA from firing on Comic media types).

### Retry Policy
**Current State:** An `IRetryPolicy` interface exists in the domain, but there is **no implementation or usage** within the infrastructure or application layers for metadata fetching.
**Evaluation:** Critical Gap. A world-class collector must handle transient network failures gracefully.
**Recommendation:** Implement an exponential backoff retry policy (e.g., using Polly) wrapping all HTTP calls, specifically handling 5xx errors and transient network exceptions.

### Cookie Handling & Age Verification
**Current State:** Implemented via `ICookieProvider` and policies (`FanzaCookiePolicy`, `MgsCookiePolicy`, `Fc2CookiePolicy`). It supports reading from `.txt` files or Playwright `storageState.json` exports. Age verification detection exists (e.g., checking for "age_check" strings).
**Evaluation:** Strong but brittle. Relying on users to manually export cookies via DevTools to specific AppData files is not user-friendly for a widely distributed app.
**Recommendation:** Integrate an automated or UI-guided interactive login/cookie extraction mechanism within the app.

### Browser Automation & Cloudflare Strategy
**Current State:** `FanzaMetadataProvider` uses Playwright to execute JavaScript and extract `__NEXT_DATA__` JSON for high-fidelity data. `JavBusMetadataProvider` detects Cloudflare (`cf-browser-verification`) but currently only logs a warning and fails (`FutureStrategy=Browser`).
**Evaluation:** Incomplete. Fanza implementation is great, but Cloudflare blocks on JavBus are fatal.
**Recommendation:** Extend Playwright usage to dynamically handle Cloudflare challenges across providers (like JavBus) when standard HTTP requests fail. Integrate stealth plugins for Playwright.

### Rate Limiting
**Current State:** Providers return `FailureReason.RateLimit` on 429 status codes, but there is no global rate-limiter or throttling mechanism preventing these 429s in the first place.
**Evaluation:** Weak. Sending requests until blocked is bad practice.
**Recommendation:** Implement a Token Bucket or Leaky Bucket global rate limiter per domain to throttle outgoing requests.

### HTTP Client & Session Persistence
**Current State:** Basic `HttpClient` usage without connection pooling configurations or robust proxy support. Sessions aren't maintained persistently across application restarts beyond the static cookie files.
**Evaluation:** Adequate for simple use cases, but insufficient for large-scale scraping.
**Recommendation:** Configure `HttpClientFactory` with pooled connections, automatic decompression, and proxy rotation capabilities.

### Cache
**Current State:** Cover images are cached locally (`CoverCacheRepository`). However, HTML/JSON responses from providers are not cached.
**Evaluation:** Missed opportunity. Re-scraping the same URL during development or retries wastes resources and risks bans.
**Recommendation:** Implement an HTTP response cache (e.g., using Redis or SQLite) with appropriate TTLs to cache provider HTML/JSON responses.

### Diagnostics
**Current State:** Logging is extensive. `ProviderDiagnostic` entity exists in the schema to track successes and failures.
**Evaluation:** Good foundation.
**Recommendation:** Expose these diagnostics in a dashboard so users know *why* a provider is failing (e.g., "MGS cookie expired").

## 3. Provider Coverage & Priority Strategy

### Provider Coverage Evaluation
- **FANZA:** Excellent coverage using Playwright to extract React state (`__NEXT_DATA__`).
- **FC2:** Basic coverage. Relies on cookies.
- **DLSite:** Implemented and handles adult/all-ages routing.
- **Getchu:** Basic implementation present.
- **MGS (MGStage):** Implemented with cookie support.
- **JAVBus:** Implemented but highly vulnerable to Cloudflare.
- **Official Maker Sites:** **Missing**. No providers exist for SOD, Prestige, S1, etc.
- **JAVLibrary:** **Missing**. Not implemented.
- **Wikis (AvWiki):** Implemented as a fallback.

### Suggested Best Provider Order
To maximize metadata quality while minimizing unnecessary requests, the execution order should be based on authoritative data sources.

**Video MediaType:**
1. `Manual` (Priority 100) - User overrides.
2. `FANZA` (Priority 80) - Primary commercial database.
3. `DLSite` (Priority 80) - Primary doujin/indie database.
4. `Mgs` (Priority 70) - Alternative commercial database.
5. `FC2` (Priority 60) - Primary FC2 PPV database.
6. `JavBus` (Priority 50) - Excellent aggregator, but lower priority than official sources due to Cloudflare flakiness.
7. `JAVLibrary` (Priority 45) - (When implemented) Great aggregator, similar to JavBus.
8. `AvWiki` (Priority 40) - Good for actress data fallback.

**Comic/Book MediaType:**
1. `Manual` (Priority 100)
2. `DLSite` (Priority 80)
3. `Getchu` (Priority 70)
4. `Melonbooks` (Priority 60) - (When implemented)
5. `ComicInfoXml` (Priority 45) - Local file data.

## 4. Conclusion
WISE has the structural potential to become a world-class metadata collector. The `MetadataConflictResolver` and fallback designs are excellent. However, to truly excel, it **must** implement robust HTTP retries, proactive rate limiting, response caching, and dynamic Cloudflare bypass (extending Playwright beyond just Fanza). Expanding provider coverage to include JavLibrary and official maker sites is also highly recommended.
