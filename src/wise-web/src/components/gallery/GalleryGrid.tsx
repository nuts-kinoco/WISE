"use client";

import { useRef, useEffect } from "react";
import { useWindowVirtualizer } from "@tanstack/react-virtual";
import { useWorks } from "@/hooks/useWorks";
import { WorkCard } from "./WorkCard";
import { useGalleryStore } from "@/store/useGalleryStore";
import { Loader2 } from "lucide-react";

export function GalleryGrid() {
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, status } = useWorks();
  const cardSize = useGalleryStore((state) => state.cardSize);

  // Flatten the pages array
  const allWorks = data ? data.pages.flatMap((page) => page.items) : [];

  // Determine grid columns based on card size
  const getColumnCount = () => {
    if (typeof window === "undefined") return 4;
    const width = window.innerWidth;
    
    // Breakpoints logic mixed with user preference
    let baseCols = 4;
    if (width < 640) baseCols = 2;
    else if (width < 1024) baseCols = 3;
    else if (width < 1536) baseCols = 5;
    else baseCols = 7;

    if (cardSize === 'sm') return baseCols + 2;
    if (cardSize === 'lg') return Math.max(2, baseCols - 1);
    return baseCols;
  };

  const columnCount = getColumnCount();
  const rowCount = Math.ceil(allWorks.length / columnCount);

  // Calculate row height (assuming 2:3 aspect ratio roughly)
  const getRowHeight = () => {
    if (typeof window === "undefined") return 300;
    const width = window.innerWidth;
    const colWidth = width / columnCount;
    return colWidth * 1.5; // height = 1.5 * width
  };

  const virtualizer = useWindowVirtualizer({
    count: rowCount,
    estimateSize: () => getRowHeight(),
    overscan: 5,
  });

  const virtualItems = virtualizer.getVirtualItems();

  // Infinite scroll trigger
  useEffect(() => {
    const [lastItem] = [...virtualItems].reverse();
    if (!lastItem) return;

    if (lastItem.index >= rowCount - 1 && hasNextPage && !isFetchingNextPage) {
      fetchNextPage();
    }
  }, [hasNextPage, fetchNextPage, virtualItems, isFetchingNextPage, rowCount]);

  if (status === 'pending') {
    return (
      <div className="flex items-center justify-center min-h-[50vh]">
        <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh] text-destructive">
        <p className="font-bold text-xl mb-2">Failed to load works</p>
        <p className="text-muted-foreground">Please check if the API is running.</p>
      </div>
    );
  }

  if (allWorks.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh]">
        <p className="text-2xl font-bold text-muted-foreground">No works found.</p>
        <p className="text-muted-foreground">Try adjusting your search or add new content.</p>
      </div>
    );
  }

  return (
    <div
      style={{
        height: `${virtualizer.getTotalSize()}px`,
        width: '100%',
        position: 'relative',
      }}
    >
      {virtualItems.map((virtualRow) => {
        const fromIndex = virtualRow.index * columnCount;
        const toIndex = Math.min(fromIndex + columnCount, allWorks.length);
        const rowWorks = allWorks.slice(fromIndex, toIndex);

        return (
          <div
            key={virtualRow.index}
            style={{
              position: 'absolute',
              top: 0,
              left: 0,
              width: '100%',
              height: `${virtualRow.size}px`,
              transform: `translateY(${virtualRow.start}px)`,
              display: 'flex',
            }}
          >
            {rowWorks.map((work, idx) => (
              <WorkCard
                key={work.id}
                work={work}
                style={{ width: `${100 / columnCount}%`, height: '100%' }}
              />
            ))}
          </div>
        );
      })}

      {isFetchingNextPage && (
        <div className="absolute bottom-0 left-0 right-0 flex justify-center p-4">
          <Loader2 className="w-6 h-6 animate-spin text-muted-foreground" />
        </div>
      )}
    </div>
  );
}
