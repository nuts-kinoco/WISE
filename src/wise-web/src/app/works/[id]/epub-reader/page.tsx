"use client";

import { use, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import {
  ArrowLeft, ChevronLeft, ChevronRight,
  Maximize2, Minimize2, Sun, Moon, Type,
} from "lucide-react";
import { API_BASE_URL, fetchReadingHistory, saveReadingHistory } from "@/lib/api";
import { useDeviceId } from "@/hooks/useDeviceId";

// ─────────────────────────────────────────────────────────────────────────────
// Types — mirrors epubjs Rendition / Book shapes we actually use
// ─────────────────────────────────────────────────────────────────────────────
type EpubRendition = {
  display: (cfi?: string) => Promise<void>;
  next: () => Promise<void>;
  prev: () => Promise<void>;
  themes: {
    fontSize: (size: string) => void;
    register: (name: string, theme: Record<string, unknown>) => void;
    select: (name: string) => void;
  };
  on: (event: string, handler: (...args: unknown[]) => void) => void;
  destroy: () => void;
};

type EpubBook = {
  renderTo: (element: HTMLElement, opts: Record<string, unknown>) => EpubRendition;
  locations: {
    generate: (chars: number) => Promise<unknown>;
    percentageFromCfi: (cfi: string) => number;
    cfiFromPercentage: (pct: number) => string;
  };
  ready: Promise<void>;
  destroy: () => void;
};

type Theme = "dark" | "light" | "sepia";

const THEMES: Record<Theme, Record<string, string>> = {
  dark:  { "body,html": "background:#111;color:#ddd;", "a": "color:#7cb9e8;" },
  light: { "body,html": "background:#fafafa;color:#222;", "a": "color:#1a6abf;" },
  sepia: { "body,html": "background:#f4ecd8;color:#3b2e1e;", "a": "color:#7a4a00;" },
};

// ─────────────────────────────────────────────────────────────────────────────

export default function EpubReaderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const deviceId = useDeviceId();

  const viewerRef = useRef<HTMLDivElement>(null);
  const bookRef = useRef<EpubBook | null>(null);
  const renditionRef = useRef<EpubRendition | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const [isReady, setIsReady] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showControls, setShowControls] = useState(true);
  const [showSettings, setShowSettings] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [theme, setTheme] = useState<Theme>("dark");
  const [fontSize, setFontSize] = useState(100); // percent
  const [resumedFromHistory, setResumedFromHistory] = useState(false);

  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const positionPctRef = useRef<number>(0);

  // ── Initialise epub.js after mount ──────────────────────────────────────────

  useEffect(() => {
    if (!viewerRef.current) return;

    let destroyed = false;

    (async () => {
      try {
        const ePub = (await import("epubjs")).default as (url: string) => EpubBook;
        const epubUrl = `${API_BASE_URL}/works/${id}/epub`;

        const book = ePub(epubUrl);
        bookRef.current = book;

        const rendition = book.renderTo(viewerRef.current!, {
          width: "100%",
          height: "100%",
          spread: "none",
          flow: "paginated",
        });
        renditionRef.current = rendition;

        // Register themes
        for (const [name, rules] of Object.entries(THEMES)) {
          rendition.themes.register(name, { "body,html": rules["body,html"], a: rules["a"] });
        }
        rendition.themes.select(theme);
        rendition.themes.fontSize(`${fontSize}%`);

        // Track position
        rendition.on("relocated", (location: unknown) => {
          const loc = location as { start?: { cfi?: string } };
          if (loc?.start?.cfi) {
            positionPctRef.current = book.locations.percentageFromCfi(loc.start.cfi);
          }

          if (!resumedFromHistory) return;
          if (saveTimer.current) clearTimeout(saveTimer.current);
          saveTimer.current = setTimeout(() => {
            saveReadingHistory(id, {
              deviceId,
              positionPercent: positionPctRef.current,
            }).catch(() => {});
          }, 2000);
        });

        await book.ready;

        // Resume from saved position
        let resumeCfi: string | undefined;
        if (deviceId) {
          const history = await fetchReadingHistory(id, deviceId).catch(() => null);
          if (history?.positionPercent != null && history.positionPercent > 0) {
            await book.locations.generate(1600);
            resumeCfi = book.locations.cfiFromPercentage(history.positionPercent);
          }
        }

        if (!destroyed) {
          await rendition.display(resumeCfi);
          setResumedFromHistory(true);
          setIsReady(true);
          // Generate locations in background for future position tracking
          if (!resumeCfi) book.locations.generate(1600).catch(() => {});
        }
      } catch (e) {
        if (!destroyed) setError(String(e));
      }
    })();

    return () => {
      destroyed = true;
      bookRef.current?.destroy();
      bookRef.current = null;
      renditionRef.current = null;
    };
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Theme / font size changes ────────────────────────────────────────────────

  useEffect(() => {
    if (!renditionRef.current) return;
    renditionRef.current.themes.select(theme);
  }, [theme]);

  useEffect(() => {
    if (!renditionRef.current) return;
    renditionRef.current.themes.fontSize(`${fontSize}%`);
  }, [fontSize]);

  // ── Navigation ───────────────────────────────────────────────────────────────

  const next = useCallback(() => renditionRef.current?.next(), []);
  const prev = useCallback(() => renditionRef.current?.prev(), []);

  // ── Keyboard ─────────────────────────────────────────────────────────────────

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      switch (e.key) {
        case "ArrowRight": next(); break;
        case "ArrowLeft":  prev(); break;
        case "f": case "F": toggleFullscreen(); break;
        case "Escape": if (isFullscreen) document.exitFullscreen(); break;
        case "+": setFontSize((s) => Math.min(200, s + 10)); break;
        case "-": setFontSize((s) => Math.max(70, s - 10)); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [next, prev, isFullscreen]);

  // ── Auto-hide controls ────────────────────────────────────────────────────────

  const resetHideTimer = useCallback(() => {
    setShowControls(true);
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => setShowControls(false), 3000);
  }, []);

  useEffect(() => {
    resetHideTimer();
    return () => { if (hideTimer.current) clearTimeout(hideTimer.current); };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Fullscreen ────────────────────────────────────────────────────────────────

  const toggleFullscreen = useCallback(() => {
    if (!document.fullscreenElement) {
      containerRef.current?.requestFullscreen();
      setIsFullscreen(true);
    } else {
      document.exitFullscreen();
      setIsFullscreen(false);
    }
  }, []);

  useEffect(() => {
    const handler = () => setIsFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", handler);
    return () => document.removeEventListener("fullscreenchange", handler);
  }, []);

  // ── Theme background colour ───────────────────────────────────────────────────

  const bgColor = theme === "dark" ? "#111" : theme === "sepia" ? "#f4ecd8" : "#fafafa";

  // ─────────────────────────────────────────────────────────────────────────────

  if (error) return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-black text-white/60 gap-4">
      <p>EPUBを読み込めませんでした</p>
      <p className="text-xs text-white/30">{error}</p>
      <Link href={`/works/${id}`} className="text-primary underline text-sm">作品詳細へ戻る</Link>
    </div>
  );

  return (
    <div
      ref={containerRef}
      className="relative flex flex-col min-h-screen select-none"
      style={{ background: bgColor }}
      onMouseMove={resetHideTimer}
      onClick={resetHideTimer}
    >
      {/* ── Top bar ── */}
      <div className={`absolute top-0 inset-x-0 z-50 flex items-center gap-3 px-4 py-3
        bg-gradient-to-b from-black/60 to-transparent transition-opacity duration-300
        ${showControls ? "opacity-100" : "opacity-0 pointer-events-none"}`}
      >
        <Link href={`/works/${id}`} className="text-white/70 hover:text-white transition-colors">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <span className="text-white/50 text-xs ml-2">EPUB Reader</span>

        {!isReady && (
          <span className="text-white/40 text-xs ml-2 animate-pulse">読み込み中…</span>
        )}

        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={() => setShowSettings((v) => !v)}
            title="設定"
            className="text-white/60 hover:text-white transition-colors"
          >
            <Type className="w-4 h-4" />
          </button>
          <button onClick={toggleFullscreen} title="フルスクリーン" className="text-white/60 hover:text-white transition-colors">
            {isFullscreen ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
          </button>
        </div>
      </div>

      {/* ── Settings panel ── */}
      {showSettings && (
        <div className="absolute top-14 right-4 z-50 bg-neutral-900 border border-white/10 rounded-xl p-4 flex flex-col gap-3 min-w-[220px]">
          {/* Theme */}
          <div className="flex items-center justify-between">
            <span className="text-white/70 text-sm">テーマ</span>
            <div className="flex gap-1">
              {(["dark", "light", "sepia"] as Theme[]).map((t) => (
                <button
                  key={t}
                  onClick={() => setTheme(t)}
                  title={t}
                  className={`w-6 h-6 rounded-full border-2 transition-all
                    ${theme === t ? "border-primary scale-110" : "border-transparent"}
                    ${t === "dark" ? "bg-neutral-900" : t === "light" ? "bg-white" : "bg-amber-100"}`}
                />
              ))}
            </div>
          </div>
          {/* Font size */}
          <div className="flex items-center justify-between gap-2">
            <span className="text-white/70 text-sm">文字サイズ</span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setFontSize((s) => Math.max(70, s - 10))}
                className="w-6 h-6 flex items-center justify-center rounded bg-white/10 text-white/60 hover:bg-white/20"
              >A</button>
              <span className="text-white/50 text-xs w-9 text-center">{fontSize}%</span>
              <button
                onClick={() => setFontSize((s) => Math.min(200, s + 10))}
                className="w-7 h-7 flex items-center justify-center rounded bg-white/10 text-white/60 hover:bg-white/20 text-lg leading-none"
              >A</button>
            </div>
          </div>
          <div className="text-white/30 text-[11px] border-t border-white/10 pt-2">
            ← → ページ移動　F フルスクリーン　+/- 文字サイズ
          </div>
        </div>
      )}

      {/* ── Epub viewer area ── */}
      <div className="flex-1 relative">
        <div
          ref={viewerRef}
          className="absolute inset-0"
          style={{ opacity: isReady ? 1 : 0, transition: "opacity 0.3s" }}
        />
        {!isReady && !error && (
          <div className="absolute inset-0 flex items-center justify-center">
            <div className="text-white/30 text-sm animate-pulse">EPUBを読み込み中…</div>
          </div>
        )}
      </div>

      {/* ── Click zones for navigation ── */}
      <div className="absolute inset-0 flex pointer-events-none z-20" style={{ top: "52px", bottom: "52px" }}>
        <button
          className="w-1/3 h-full pointer-events-auto cursor-pointer"
          onClick={prev}
          aria-label="前のページ"
        />
        <div className="flex-1" />
        <button
          className="w-1/3 h-full pointer-events-auto cursor-pointer"
          onClick={next}
          aria-label="次のページ"
        />
      </div>

      {/* ── Bottom bar ── */}
      <div className={`absolute bottom-0 inset-x-0 z-50 flex items-center justify-center gap-6 px-6 py-3
        bg-gradient-to-t from-black/60 to-transparent transition-opacity duration-300
        ${showControls ? "opacity-100" : "opacity-0 pointer-events-none"}`}
      >
        <button onClick={prev} className="text-white/60 hover:text-white transition-colors">
          <ChevronLeft className="w-6 h-6" />
        </button>
        <span className="text-white/30 text-xs">← →</span>
        <button onClick={next} className="text-white/60 hover:text-white transition-colors">
          <ChevronRight className="w-6 h-6" />
        </button>
      </div>
    </div>
  );
}
