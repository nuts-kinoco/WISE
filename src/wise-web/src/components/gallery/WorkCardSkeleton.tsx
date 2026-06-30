"use client";

import { Skeleton } from "@/components/ui/Skeleton";
import type { Density } from "@/store/useGalleryStore";

interface Props {
  style?: React.CSSProperties;
  density: Density;
  isLandscape: boolean;
}

export function WorkCardSkeleton({ style, density, isLandscape }: Props) {
  if (density === "list") {
    return (
      <div style={style} className="flex items-center gap-3 px-5 border-b border-border/30">
        <Skeleton className="flex-none w-10 h-10 rounded" />
        <Skeleton className="flex-none w-[110px] h-3" />
        <Skeleton className="flex-1 h-3" />
        <Skeleton className="flex-none w-[140px] h-3 hidden md:block" />
        <Skeleton className="flex-none w-[88px] h-3" />
        <Skeleton className="flex-none w-[80px] h-3" />
      </div>
    );
  }

  const aspectClass = isLandscape ? "aspect-video" : "aspect-[2/3]";

  return (
    <div style={style} className="p-1.5">
      <Skeleton className={`w-full ${aspectClass} rounded-lg`} />
      {density !== "compact" && (
        <div className="mt-2 px-0.5 space-y-1.5">
          <Skeleton className="h-3 w-full" />
          <Skeleton className="h-3 w-2/3" />
          {density === "rich" && (
            <>
              <Skeleton className="h-2.5 w-1/2 mt-1" />
              <Skeleton className="h-2.5 w-3/5" />
            </>
          )}
        </div>
      )}
    </div>
  );
}
