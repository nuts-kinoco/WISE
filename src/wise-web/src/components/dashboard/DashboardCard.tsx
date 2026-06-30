"use client";

import Image from "next/image";
import Link from "next/link";
import { Star } from "lucide-react";
import { type WorkItem } from "@/lib/api";
import { resolveCoverUrl } from "@/lib/media";

const MEDIA_LABEL: Record<string, string> = {
  Video: "動画", Comic: "コミック", Book: "書籍",
  PhotoBook: "写真集", ImageCollection: "画像集", Audio: "音声",
};

interface Props {
  work: WorkItem;
  badge?: string;
}

export function DashboardCard({ work, badge }: Props) {
  const src = resolveCoverUrl(work.coverUrl);
  const sub = work.actress ?? work.author ?? work.circle ?? work.maker ?? null;

  return (
    <Link
      href={`/works/${work.id}`}
      className="flex-none w-36 group block"
    >
      {/* Cover */}
      <div className="relative w-full aspect-[2/3] rounded-lg overflow-hidden bg-muted/40">
        {src ? (
          <Image
            src={src}
            alt={work.title ?? ""}
            fill
            className="object-cover transition-transform duration-300 group-hover:scale-105"
            unoptimized
            sizes="144px"
          />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center text-muted-foreground/30 text-xs">
            {MEDIA_LABEL[work.mediaType] ?? ""}
          </div>
        )}

        {/* Badge */}
        {badge && (
          <div className="absolute top-1.5 left-1.5">
            <span className="text-[10px] font-medium bg-primary/90 text-primary-foreground px-1.5 py-0.5 rounded-full">
              {badge}
            </span>
          </div>
        )}

        {/* Rating */}
        {work.rating != null && (
          <div className="absolute bottom-1.5 right-1.5 flex items-center gap-0.5 bg-black/60 rounded px-1 py-0.5">
            <Star className="w-2.5 h-2.5 text-amber-400 fill-amber-400" />
            <span className="text-[10px] text-white font-medium">{work.rating}</span>
          </div>
        )}
      </div>

      {/* Info */}
      <div className="mt-1.5 px-0.5">
        <p className="text-[12px] font-medium leading-snug line-clamp-2 text-foreground group-hover:text-primary transition-colors">
          {work.title ?? work.primaryIdentifier ?? "—"}
        </p>
        {sub && (
          <p className="text-[11px] text-muted-foreground mt-0.5 truncate">{sub}</p>
        )}
      </div>
    </Link>
  );
}
