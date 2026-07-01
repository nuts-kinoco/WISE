"use client";

import { useCallback, useEffect } from "react";
import { useWindowVirtualizer } from "@tanstack/react-virtual";
import { useWorks } from "@/hooks/useWorks";
import { WorkCard } from "./WorkCard";
import { WorkListRow } from "./WorkListRow";
import { useGalleryStore, type Density, type ListSortKey } from "@/store/useGalleryStore";
import { useWindowWidth } from "@/hooks/useWindowWidth";
import { ChevronUp, ChevronDown, ChevronsUpDown, HardDriveUpload, SearchX } from "lucide-react";
import type { WorkItem } from "@/lib/api";
import { WorkCardSkeleton } from "./WorkCardSkeleton";
import Link from "next/link";

// ── Column calculation ────────────────────────────────────────────────────────

type BpCols = [minWidth: number, cols: number][];

const BREAKPOINTS: Record<string, BpCols> = {
  "compact-portrait":  [[0, 2], [480, 3], [768, 5], [1024, 7], [1440, 9],  [1920, 11]],
  "compact-landscape": [[0, 1], [480, 2], [768, 3], [1024, 5], [1440, 7],  [1920,  9]],
  "normal-portrait":   [[0, 2], [640, 3], [1024,4], [1280, 5], [1536, 6],  [1920,  8]],
  "normal-landscape":  [[0, 1], [640, 2], [1024,3], [1280, 4], [1536, 5],  [1920,  6]],
  "rich-portrait":     [[0, 1], [640, 2], [1024,3], [1280, 4], [1536, 5],  [1920,  6]],
  "rich-landscape":    [[0, 1], [768, 2], [1280,3], [1536, 4], [1920, 5]],
};

function getColumnCount(density: Density, isLandscape: boolean, width: number): number {
  if (density === "list") return 1;
  const key = `${density}-${isLandscape ? "landscape" : "portrait"}`;
  const bps = BREAKPOINTS[key] ?? BREAKPOINTS["normal-portrait"];
  let cols = bps[0][1];
  for (const [bp, c] of bps) {
    if (width >= bp) cols = c;
  }
  return cols;
}

// Info strip heights per density (in px)
const INFO_HEIGHT: Record<Density, number> = {
  compact: 0,
  normal:  0,
  rich:    148,
  list:    72,
};

function getRowHeight(
  density: Density,
  isLandscape: boolean,
  colCount: number,
  windowWidth: number
): number {
  if (density === "list") return 72;
  const colWidth = (windowWidth - 32) / colCount;
  const coverH = isLandscape ? colWidth * (9 / 16) : colWidth * (3 / 2);
  return Math.round(coverH + INFO_HEIGHT[density] + 8);
}

// ── List sort helpers ─────────────────────────────────────────────────────────

function sortWorks(works: WorkItem[], key: ListSortKey, asc: boolean): WorkItem[] {
  if (!key) return works;
  return [...works].sort((a, b) => {
    let av = "", bv = "";
    switch (key) {
      case "identifier":  av = a.primaryIdentifier ?? ""; bv = b.primaryIdentifier ?? ""; break;
      case "title":       av = a.title ?? "";             bv = b.title ?? "";             break;
      case "actress":     av = a.actress ?? "";           bv = b.actress ?? "";           break;
      case "maker":       av = a.maker ?? "";             bv = b.maker ?? "";             break;
      case "label":       av = a.label ?? "";             bv = b.label ?? "";             break;
      case "releaseDate": av = a.releaseDate ?? "";       bv = b.releaseDate ?? "";       break;
    }
    return asc ? av.localeCompare(bv, "ja") : bv.localeCompare(av, "ja");
  });
}

function SortTh({
  label, sortKey, currentKey, asc, onSort, className = "",
}: {
  label: string; sortKey: ListSortKey; currentKey: ListSortKey; asc: boolean;
  onSort: (k: ListSortKey) => void; className?: string;
}) {
  const active = currentKey === sortKey;
  return (
    <button
      onClick={() => onSort(sortKey)}
      className={`flex items-center gap-1 text-[11px] font-semibold uppercase tracking-wide
        text-muted-foreground hover:text-foreground transition-colors ${className}`}
    >
      {label}
      {active
        ? asc ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />
        : <ChevronsUpDown className="w-3 h-3 opacity-40" />}
    </button>
  );
}

// ── Component ─────────────────────────────────────────────────────────────────

export function GalleryGrid() {
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, status } = useWorks();
  const { density, coverLayout, listSortKey, listSortAsc, setListSort, searchQuery, setSearchQuery, mediaTypeFilter } = useGalleryStore();
  const windowWidth = useWindowWidth();

  const isList = density === "list";
  const isLandscape = coverLayout === "landscape";

  const allWorks: WorkItem[] = data ? data.pages.flatMap((p) => p.items) : [];
  const displayWorks = isList ? sortWorks(allWorks, listSortKey, listSortAsc) : allWorks;

  const colCount = getColumnCount(density, isLandscape, windowWidth);
  const rowCount = Math.ceil(displayWorks.length / colCount);

  const estimateSize = useCallback(
    () => getRowHeight(density, isLandscape, colCount, windowWidth),
    [density, isLandscape, colCount, windowWidth]
  );

  const virtualizer = useWindowVirtualizer({
    count: rowCount,
    estimateSize,
    overscan: 4,
  });

  // Remeasure when layout changes (density, orientation, window width)
  useEffect(() => {
    virtualizer.measure();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [density, isLandscape, windowWidth]);

  // Infinite scroll trigger
  useEffect(() => {
    const last = virtualizer.getVirtualItems().at(-1);
    if (!last) return;
    if (last.index >= rowCount - 2 && hasNextPage && !isFetchingNextPage) {
      fetchNextPage();
    }
  }, [hasNextPage, fetchNextPage, isFetchingNextPage, rowCount, virtualizer]);

  const handleSort = (key: ListSortKey) => {
    setListSort(key, listSortKey === key ? !listSortAsc : true);
  };

  // ── Render states ───────────────────────────────────────────────────────────

  const isFiltered = !!searchQuery || !!mediaTypeFilter;

  if (status === "pending") {
    const skeletonCount = colCount * 3;
    const skeletonRows = Math.ceil(skeletonCount / colCount);
    return (
      <div className="w-full">
        {Array.from({ length: skeletonRows }).map((_, ri) => (
          <div key={ri} style={{ display: "flex" }}>
            {Array.from({ length: colCount }).map((_, ci) => (
              <WorkCardSkeleton
                key={ci}
                style={{ width: `${100 / colCount}%` }}
                density={density}
                isLandscape={isLandscape}
              />
            ))}
          </div>
        ))}
      </div>
    );
  }

  if (status === "error") {
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh] gap-3 text-center">
        <div className="w-12 h-12 rounded-full bg-destructive/10 flex items-center justify-center">
          <span className="text-2xl">!</span>
        </div>
        <p className="font-semibold text-foreground">ライブラリを読み込めませんでした</p>
        <p className="text-sm text-muted-foreground">APIサーバーが起動しているか確認してください。</p>
        <button
          onClick={() => window.location.reload()}
          className="mt-1 px-4 py-1.5 rounded-lg border border-border text-sm text-muted-foreground hover:text-foreground hover:border-foreground/30 transition-colors"
        >
          再読み込み
        </button>
      </div>
    );
  }

  if (allWorks.length === 0) {
    if (isFiltered) {
      return (
        <div className="flex flex-col items-center justify-center min-h-[50vh] gap-3 text-center">
          <SearchX className="w-10 h-10 text-muted-foreground/30" />
          <p className="font-semibold text-foreground">検索結果がありません</p>
          <p className="text-sm text-muted-foreground/70 max-w-xs">
            別のキーワードで試すか、フィルターを解除してください。
          </p>
          <button
            onClick={() => setSearchQuery("")}
            className="mt-1 px-4 py-1.5 rounded-lg border border-border text-sm text-muted-foreground hover:text-foreground hover:border-foreground/30 transition-colors"
          >
            検索をクリア
          </button>
        </div>
      );
    }
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh] gap-3 text-center">
        <HardDriveUpload className="w-10 h-10 text-muted-foreground/30" />
        <p className="font-semibold text-foreground">ライブラリが空です</p>
        <p className="text-sm text-muted-foreground/70 max-w-xs">
          作品をインポートするとここに表示されます。
        </p>
        <Link
          href="/import"
          className="mt-1 px-5 py-2 rounded-xl bg-primary text-primary-foreground text-[13px] font-medium hover:opacity-90 transition-opacity"
        >
          作品をインポートする
        </Link>
      </div>
    );
  }

  return (
    <div className="w-full">
      {/* List mode sticky sort header */}
      {isList && (
        <div className="sticky top-16 z-20 bg-background/95 backdrop-blur border-b border-border/40 mb-1">
          <div className="flex items-center gap-3 px-5 py-2">
            <div className="flex-none w-10" />
            <SortTh label="品番"    sortKey="identifier"  currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-none w-[110px]" />
            <SortTh label="タイトル" sortKey="title"       currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-1 min-w-0" />
            <SortTh label="出演者"   sortKey="actress"     currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-none w-[140px] hidden md:flex" />
            <SortTh label="メーカー" sortKey="maker"       currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-none w-[140px] hidden lg:flex" />
            <SortTh label="レーベル" sortKey="label"       currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-none w-[100px] hidden xl:flex" />
            <SortTh label="発売日"   sortKey="releaseDate" currentKey={listSortKey} asc={listSortAsc} onSort={handleSort} className="flex-none w-[88px]" />
            <div className="flex-none w-[80px]" />
          </div>
        </div>
      )}

      {/* Virtual scroll container */}
      <div
        style={{ height: `${virtualizer.getTotalSize()}px`, width: "100%", position: "relative" }}
        role="list"
        aria-label="作品一覧"
      >
        {virtualizer.getVirtualItems().map((vRow) => {
          const from = vRow.index * colCount;
          const rowWorks = displayWorks.slice(from, from + colCount);

          return (
            <div
              key={vRow.index}
              role="row"
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                height: `${vRow.size}px`,
                transform: `translateY(${vRow.start}px)`,
                display: "flex",
              }}
            >
              {isList ? (
                <WorkListRow
                  work={rowWorks[0]}
                  style={{ width: "100%", height: "100%" }}
                />
              ) : (
                rowWorks.map((work) => (
                  <WorkCard
                    key={work.id}
                    work={work}
                    style={{ width: `${100 / colCount}%`, height: "100%" }}
                  />
                ))
              )}
            </div>
          );
        })}
      </div>

      {isFetchingNextPage && (
        <div style={{ display: "flex" }}>
          {Array.from({ length: colCount }).map((_, ci) => (
            <WorkCardSkeleton
              key={ci}
              style={{ width: `${100 / colCount}%` }}
              density={density}
              isLandscape={isLandscape}
            />
          ))}
        </div>
      )}
    </div>
  );
}
