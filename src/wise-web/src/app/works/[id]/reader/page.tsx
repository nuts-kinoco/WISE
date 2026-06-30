"use client";

import { use, useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import Image from "next/image";
import Link from "next/link";
import {
  ArrowLeft, ChevronLeft, ChevronRight, Maximize2, Minimize2,
  BookOpen, LayoutTemplate, Settings,
} from "lucide-react";
import { fetchReaderPages, getReaderPageUrl, type ReaderPage } from "@/lib/api";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

type ReadingDirection = "rtl" | "ltr";
type PageMode = "single" | "double";

// ─────────────────────────────────────────────────────────────────────────────
// Page component
// ─────────────────────────────────────────────────────────────────────────────

export default function ReaderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);

  const { data, isLoading, error } = useQuery({
    queryKey: ["reader", id],
    queryFn: () => fetchReaderPages(id),
  });

  const [currentPage, setCurrentPage] = useState(0);
  const [direction, setDirection] = useState<ReadingDirection>("rtl");
  const [pageMode, setPageMode] = useState<PageMode>("single");
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [showControls, setShowControls] = useState(true);
  const [showSettings, setShowSettings] = useState(false);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const totalPages = data?.totalPages ?? 0;

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

  // ── Prefetch adjacent pages ─────────────────────────────────────────────────

  useEffect(() => {
    if (!data) return;
    const prefetch = (page: number) => {
      if (page >= 0 && page < totalPages) {
        const img = new window.Image();
        img.src = getReaderPageUrl(id, page);
      }
    };
    prefetch(currentPage + 1);
    prefetch(currentPage + 2);
    prefetch(currentPage - 1);
  }, [id, currentPage, totalPages, data]);

  // ── Render pages ────────────────────────────────────────────────────────────

  const rightPageIndex = direction === "rtl" ? currentPage : currentPage;
  const leftPageIndex  = direction === "rtl" ? currentPage + 1 : currentPage - 1;

  const pageIndexes: number[] = pageMode === "double"
    ? (direction === "rtl"
        ? [currentPage + 1, currentPage].filter((p) => p >= 0 && p < totalPages)
        : [currentPage, currentPage + 1].filter((p) => p >= 0 && p < totalPages))
    : [currentPage];

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
        <div className="absolute top-14 right-4 z-50 bg-neutral-900 border border-white/10 rounded-xl p-4 flex flex-col gap-3 min-w-[200px]">
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
          <div className="text-white/30 text-[11px] border-t border-white/10 pt-2">
            ← → ページ移動　F フルスクリーン　D 2P切替
          </div>
        </div>
      )}

      {/* ── Page display area ── */}
      <div className="flex-1 flex items-center justify-center overflow-hidden">
        <div className={`flex h-full ${pageMode === "double" ? "gap-0.5" : ""} max-h-screen`}>
          {pageIndexes.map((pi) => (
            <div
              key={pi}
              className={`relative ${pageMode === "double" ? "w-[50vw]" : "w-full max-w-[100vh]"} h-screen`}
            >
              <Image
                key={getReaderPageUrl(id, pi)}
                src={getReaderPageUrl(id, pi)}
                alt={`Page ${pi + 1}`}
                fill
                className="object-contain"
                unoptimized
                priority={pi === currentPage}
              />
            </div>
          ))}
        </div>
      </div>

      {/* ── Click zones for navigation ── */}
      <div className="absolute inset-0 flex pointer-events-none z-20">
        {/* Left zone → retreat in RTL, advance in LTR */}
        <button
          className="flex-1 h-full pointer-events-auto cursor-pointer"
          onClick={retreat}
          aria-label="前のページ"
        />
        {/* Right zone → advance in RTL, advance in LTR */}
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

        {/* Scrubber */}
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
