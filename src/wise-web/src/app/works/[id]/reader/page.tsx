"use client";

import { use, useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import Image from "next/image";
import Link from "next/link";
import {
  ArrowLeft, ChevronLeft, ChevronRight, Maximize2, Minimize2,
  BookOpen, LayoutTemplate, Settings,
} from "lucide-react";
import { fetchReaderPages, getReaderPageUrl, fetchReadingHistory, saveReadingHistory } from "@/lib/api";
import { useDeviceId } from "@/hooks/useDeviceId";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

type ReadingDirection = "rtl" | "ltr";
type PageMode = "single" | "double";
type ResizeFilter = "bilinear" | "bicubic" | "lanczos";
type ImageFilter = "none" | "soft" | "soft-sharp";

const RESIZE_FILTER_LABELS: Record<ResizeFilter, string> = {
  bilinear: "バイリニア",
  bicubic: "バイキュービック",
  lanczos: "ランチョス",
};

const IMAGE_FILTER_LABELS: Record<ImageFilter, string> = {
  none: "なし",
  soft: "ソフト",
  "soft-sharp": "ソフト＋シャープ",
};

// CSS image-rendering values per filter
const IMAGE_RENDERING: Record<ResizeFilter, string> = {
  bilinear: "auto",
  bicubic: "-webkit-optimize-contrast",
  lanczos: "high-quality",
};

// CSS filter values (soft-sharp uses the SVG filter defined inline)
const CSS_FILTER: Record<ImageFilter, string> = {
  none: "",
  soft: "blur(0.5px)",
  "soft-sharp": "url(#reader-sharpen)",
};

// ─────────────────────────────────────────────────────────────────────────────
// Page component
// ─────────────────────────────────────────────────────────────────────────────

export default function ReaderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);

  const deviceId = useDeviceId();

  const { data, isLoading, error } = useQuery({
    queryKey: ["reader", id],
    queryFn: () => fetchReaderPages(id),
  });

  const { data: history } = useQuery({
    queryKey: ["reading-history", id, deviceId],
    queryFn: () => fetchReadingHistory(id, deviceId),
    enabled: !!deviceId,
  });

  const [currentPage, setCurrentPage] = useState(0);
  const [resumedFromHistory, setResumedFromHistory] = useState(false);
  const [direction, setDirection] = useState<ReadingDirection>("rtl");
  const [pageMode, setPageMode] = useState<PageMode>("single");
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [showControls, setShowControls] = useState(true);
  const [showSettings, setShowSettings] = useState(false);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // Reader display preferences (persisted in localStorage)
  const [readerCacheMb, setReaderCacheMb] = useState<number>(1024);
  const [resizeFilter, setResizeFilter] = useState<ResizeFilter>("bilinear");
  const [imageFilter, setImageFilter] = useState<ImageFilter>("none");

  const totalPages = data?.totalPages ?? 0;

  // Load reader prefs from localStorage on mount
  useEffect(() => {
    const mb = parseInt(localStorage.getItem("wise_reader_cache_mb") ?? "1024", 10);
    setReaderCacheMb(Number.isFinite(mb) && mb > 0 ? mb : 1024);
    const rf = localStorage.getItem("wise_reader_resize_filter") ?? "bilinear";
    if (rf === "bilinear" || rf === "bicubic" || rf === "lanczos") setResizeFilter(rf);
    const imf = localStorage.getItem("wise_reader_image_filter") ?? "none";
    if (imf === "none" || imf === "soft" || imf === "soft-sharp") setImageFilter(imf);
  }, []);

  const saveResizeFilter = (v: ResizeFilter) => {
    setResizeFilter(v);
    localStorage.setItem("wise_reader_resize_filter", v);
  };

  const saveImageFilter = (v: ImageFilter) => {
    setImageFilter(v);
    localStorage.setItem("wise_reader_image_filter", v);
  };

  // Restore last-read page once both data and history are loaded
  useEffect(() => {
    if (!data || resumedFromHistory) return;
    const saved = history?.pageNumber;
    if (saved != null && saved > 0 && saved < data.totalPages) {
      setCurrentPage(saved);
    }
    setResumedFromHistory(true);
  }, [data, history, resumedFromHistory]);

  // Debounced save: write to backend 2s after page change
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (!resumedFromHistory || !deviceId) return;
    if (saveTimer.current) clearTimeout(saveTimer.current);
    const isFinished = totalPages > 0 && currentPage >= totalPages - 1;
    saveTimer.current = setTimeout(() => {
      saveReadingHistory(id, {
        deviceId,
        pageNumber: currentPage,
        positionPercent: isFinished ? 1.0 : null,
      }).catch(() => {});
    }, 2000);
    return () => { if (saveTimer.current) clearTimeout(saveTimer.current); };
  }, [id, currentPage, deviceId, resumedFromHistory, totalPages]);

  // ── Navigation ──────────────────────────────────────────────────────────────

  const goTo = useCallback((page: number) => {
    if (page < 0 || page >= totalPages) return;
    setCurrentPage(page);
  }, [totalPages]);

  const advance = useCallback(() => {
    const step = pageMode === "double" ? 2 : 1;
    if (direction === "rtl") goTo(currentPage - step);
    else goTo(currentPage + step);
  }, [currentPage, direction, pageMode, goTo]);

  const retreat = useCallback(() => {
    const step = pageMode === "double" ? 2 : 1;
    if (direction === "rtl") goTo(currentPage + step);
    else goTo(currentPage - step);
  }, [currentPage, direction, pageMode, goTo]);

  // ── Keyboard shortcuts ──────────────────────────────────────────────────────

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      switch (e.key) {
        case "ArrowRight": advance(); break;
        case "ArrowLeft":  retreat(); break;
        case "ArrowUp":    goTo(0); break;
        case "ArrowDown":  goTo(totalPages - 1); break;
        case "f": case "F": toggleFullscreen(); break;
        case "d": case "D": setPageMode((m) => m === "single" ? "double" : "single"); break;
        case "Escape": if (isFullscreen) document.exitFullscreen(); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [advance, retreat, goTo, totalPages, isFullscreen]);

  // ── Auto-hide controls ──────────────────────────────────────────────────────

  const resetHideTimer = useCallback(() => {
    setShowControls(true);
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => setShowControls(false), 3000);
  }, []);

  useEffect(() => {
    resetHideTimer();
    return () => { if (hideTimer.current) clearTimeout(hideTimer.current); };
  }, []);

  // ── Fullscreen ──────────────────────────────────────────────────────────────

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

  // ── Prefetch adjacent pages (window based on cache MB setting) ──────────────

  useEffect(() => {
    if (!data) return;
    // Assume ~10MB per page; cap at 50 pages
    const prefetchWindow = Math.min(50, Math.max(2, Math.floor(readerCacheMb / 10)));
    const prefetch = (page: number) => {
      if (page >= 0 && page < totalPages) {
        const img = new window.Image();
        img.src = getReaderPageUrl(id, page);
      }
    };
    for (let i = 1; i <= prefetchWindow; i++) prefetch(currentPage + i);
    prefetch(currentPage - 1);
  }, [id, currentPage, totalPages, data, readerCacheMb]);

  // ── Render pages ────────────────────────────────────────────────────────────

  const pageIndexes: number[] = pageMode === "double"
    ? (direction === "rtl"
        ? [currentPage + 1, currentPage].filter((p) => p >= 0 && p < totalPages)
        : [currentPage, currentPage + 1].filter((p) => p >= 0 && p < totalPages))
    : [currentPage];

  const imgStyle: React.CSSProperties = {
    imageRendering: IMAGE_RENDERING[resizeFilter] as React.CSSProperties["imageRendering"],
    ...(CSS_FILTER[imageFilter] ? { filter: CSS_FILTER[imageFilter] } : {}),
  };

  // ─────────────────────────────────────────────────────────────────────────

  if (isLoading) return (
    <div className="flex items-center justify-center min-h-screen bg-black text-white/50">
      読み込み中…
    </div>
  );

  if (error || !data) return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-black text-white/60 gap-4">
      <p>アーカイブを読み込めませんでした</p>
      <Link href={`/works/${id}`} className="text-primary underline text-sm">作品詳細へ戻る</Link>
    </div>
  );

  return (
    <div
      ref={containerRef}
      className="relative flex flex-col min-h-screen bg-black select-none"
      onMouseMove={resetHideTimer}
      onClick={resetHideTimer}
    >
      {/* SVG filter definitions for soft-sharp mode */}
      <svg style={{ position: "absolute", width: 0, height: 0, overflow: "hidden" }} aria-hidden>
        <defs>
          <filter id="reader-sharpen" colorInterpolationFilters="sRGB">
            <feConvolveMatrix
              order="3"
              kernelMatrix="-1 -1 -1  -1 9 -1  -1 -1 -1"
              preserveAlpha="true"
            />
          </filter>
        </defs>
      </svg>

      {/* ── Top bar ── */}
      <div className={`absolute top-0 inset-x-0 z-50 flex items-center gap-3 px-4 py-3
        bg-gradient-to-b from-black/80 to-transparent transition-opacity duration-300
        ${showControls ? "opacity-100" : "opacity-0 pointer-events-none"}`}
      >
        <Link href={`/works/${id}`} className="text-white/70 hover:text-white transition-colors">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <span className="text-white/50 text-sm font-mono ml-2">
          {currentPage + 1} / {totalPages}
          {pageMode === "double" && currentPage + 1 < totalPages
            ? ` – ${currentPage + 2} / ${totalPages}` : ""}
        </span>
        {history?.pageNumber != null && history.pageNumber === currentPage && (
          <span className="text-[11px] bg-primary/20 text-primary px-2 py-0.5 rounded-full ml-2">
            前回の続き
          </span>
        )}
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={() => setShowSettings((v) => !v)}
            title="設定"
            className="text-white/60 hover:text-white transition-colors"
          >
            <Settings className="w-4.5 h-4.5" />
          </button>
          <button onClick={toggleFullscreen} title="フルスクリーン" className="text-white/60 hover:text-white transition-colors">
            {isFullscreen ? <Minimize2 className="w-4.5 h-4.5" /> : <Maximize2 className="w-4.5 h-4.5" />}
          </button>
        </div>
      </div>

      {/* ── Settings panel ── */}
      {showSettings && (
        <div className="absolute top-14 right-4 z-50 bg-neutral-900 border border-white/10 rounded-xl p-4 flex flex-col gap-3 min-w-[220px]">
          {/* Direction */}
          <div className="flex items-center justify-between">
            <span className="text-white/70 text-sm">読む方向</span>
            <div className="flex gap-1">
              {(["rtl", "ltr"] as const).map((d) => (
                <button
                  key={d}
                  onClick={() => setDirection(d)}
                  className={`px-2.5 py-1 text-xs rounded-lg transition-colors
                    ${direction === d ? "bg-primary text-white" : "bg-white/10 text-white/60 hover:bg-white/20"}`}
                >
                  {d === "rtl" ? "右→左" : "左→右"}
                </button>
              ))}
            </div>
          </div>
          {/* Page mode */}
          <div className="flex items-center justify-between">
            <span className="text-white/70 text-sm">表示モード</span>
            <div className="flex gap-1">
              <button
                onClick={() => setPageMode("single")}
                className={`px-2.5 py-1 text-xs rounded-lg transition-colors
                  ${pageMode === "single" ? "bg-primary text-white" : "bg-white/10 text-white/60 hover:bg-white/20"}`}
              >
                <BookOpen className="w-3.5 h-3.5 inline mr-1" />1P
              </button>
              <button
                onClick={() => setPageMode("double")}
                className={`px-2.5 py-1 text-xs rounded-lg transition-colors
                  ${pageMode === "double" ? "bg-primary text-white" : "bg-white/10 text-white/60 hover:bg-white/20"}`}
              >
                <LayoutTemplate className="w-3.5 h-3.5 inline mr-1" />2P
              </button>
            </div>
          </div>
          {/* Resize filter */}
          <div className="flex items-center justify-between gap-3">
            <span className="text-white/70 text-sm shrink-0">リサイズ</span>
            <select
              value={resizeFilter}
              onChange={(e) => saveResizeFilter(e.target.value as ResizeFilter)}
              className="bg-white/10 text-white/80 text-xs rounded-lg px-2 py-1 focus:outline-none cursor-pointer"
            >
              {(Object.keys(RESIZE_FILTER_LABELS) as ResizeFilter[]).map((k) => (
                <option key={k} value={k} className="bg-neutral-900">{RESIZE_FILTER_LABELS[k]}</option>
              ))}
            </select>
          </div>
          {/* Image filter */}
          <div className="flex items-center justify-between gap-3">
            <span className="text-white/70 text-sm shrink-0">フィルター</span>
            <select
              value={imageFilter}
              onChange={(e) => saveImageFilter(e.target.value as ImageFilter)}
              className="bg-white/10 text-white/80 text-xs rounded-lg px-2 py-1 focus:outline-none cursor-pointer"
            >
              {(Object.keys(IMAGE_FILTER_LABELS) as ImageFilter[]).map((k) => (
                <option key={k} value={k} className="bg-neutral-900">{IMAGE_FILTER_LABELS[k]}</option>
              ))}
            </select>
          </div>
          <div className="text-white/30 text-[11px] border-t border-white/10 pt-2">
            ← → ページ移動　F フルスクリーン　D 2P切替
          </div>
        </div>
      )}

      {/* ── Page display area ── */}
      <div className="flex-1 overflow-hidden bg-black">
        {/* 2P: grid で厳密に50/50分割 — flex+w-[50vw]はサブピクセル境界で白線が出る */}
        <div className={`h-full w-full ${pageMode === "double" ? "grid grid-cols-2" : "flex items-center justify-center"}`}>
          {pageIndexes.map((pi) => (
            <div
              key={pi}
              className={`relative bg-black ${pageMode === "double" ? "h-full" : "max-w-[100vh] w-screen h-full"}`}
            >
              <Image
                key={getReaderPageUrl(id, pi)}
                src={getReaderPageUrl(id, pi)}
                alt={`Page ${pi + 1}`}
                fill
                className="object-contain"
                style={imgStyle}
                unoptimized
                priority={pi === currentPage}
              />
            </div>
          ))}
        </div>
      </div>

      {/* ── Click zones for navigation ── */}
      <div className="absolute inset-0 flex pointer-events-none z-20">
        <button
          className="flex-1 h-full pointer-events-auto cursor-pointer"
          onClick={retreat}
          aria-label="前のページ"
        />
        <button
          className="flex-1 h-full pointer-events-auto cursor-pointer"
          onClick={advance}
          aria-label="次のページ"
        />
      </div>

      {/* ── Bottom bar ── */}
      <div className={`absolute bottom-0 inset-x-0 z-50 flex items-center gap-3 px-4 py-3
        bg-gradient-to-t from-black/80 to-transparent transition-opacity duration-300
        ${showControls ? "opacity-100" : "opacity-0 pointer-events-none"}`}
      >
        <button onClick={retreat} className="text-white/60 hover:text-white transition-colors">
          <ChevronLeft className="w-5 h-5" />
        </button>

        <input
          type="range"
          min={0}
          max={Math.max(0, totalPages - 1)}
          value={currentPage}
          onChange={(e) => goTo(Number(e.target.value))}
          className="flex-1 h-1 accent-primary cursor-pointer"
        />

        <button onClick={advance} className="text-white/60 hover:text-white transition-colors">
          <ChevronRight className="w-5 h-5" />
        </button>
      </div>
    </div>
  );
}
