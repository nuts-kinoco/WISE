"use client";

import Image from "next/image";
import Link from "next/link";
import { Trash2, Heart } from "lucide-react";
import { deleteWork, patchUserData, type WorkItem } from "@/lib/api";
import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { resolveCoverUrl } from "@/lib/media";

interface Props {
  work: WorkItem;
  style?: React.CSSProperties;
}

const STATUS_DOT: Record<string, string> = {
  Organizing:       "bg-emerald-500",
  Failed:           "bg-rose-500",
  ScanPending:      "bg-amber-400",
  MetadataFetching: "bg-blue-400",
};

export function WorkListRow({ work, style }: Props) {
  const queryClient = useQueryClient();
  const [confirm, setConfirm] = useState(false);
  const [fav, setFav] = useState(work.favorite);
  const src = resolveCoverUrl(work.coverUrl);
  const hasData = !!(work.title || work.actress);
  const isPending = !hasData
    && work.metadataStatus !== "Failed"
    && work.metadataStatus !== "NotFound"
    && work.metadataStatus !== "Organizing";

  const handleFav = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const next = !fav;
    setFav(next);
    try { await patchUserData(work.id, { favorite: next, rating: work.rating }); }
    catch { setFav(!next); }
  };

  const handleDelete = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!confirm) { setConfirm(true); return; }
    try {
      await deleteWork(work.id, false);
      await queryClient.invalidateQueries({ queryKey: ["works"] });
    } catch { setConfirm(false); }
  };

  const dotColor = STATUS_DOT[work.metadataStatus] ?? "bg-muted-foreground/30";

  return (
    <div style={style} className="px-2">
      <Link
        href={`/works/${work.id}`}
        className="flex items-center gap-3 h-full px-3 py-1.5 rounded-xl
          bg-card border border-border/30
          hover:border-primary/30 hover:bg-muted/20
          transition-all group"
      >
        {/* Thumbnail */}
        <div className="flex-none w-10 h-[58px] rounded-lg overflow-hidden bg-muted relative">
          {src ? (
            <Image
              src={src}
              alt={work.primaryIdentifier ?? ""}
              fill
              sizes="40px"
              className="object-cover"
              loading="lazy"
              unoptimized={src.startsWith("http://localhost")}
            />
          ) : (
            <div className={`absolute inset-0 ${isPending ? "bg-muted/60 animate-pulse" : "bg-slate-800/80"}`} />
          )}
        </div>

        {/* Identifier */}
        <span className="flex-none w-[110px] text-[11px] font-mono text-primary/70 uppercase tracking-wider truncate">
          {work.primaryIdentifier ?? "—"}
        </span>

        {/* Title */}
        <span className="flex-1 min-w-0 text-sm text-foreground truncate">
          {isPending
            ? <span className="inline-block h-3 w-48 bg-muted/60 rounded animate-pulse" />
            : work.title
              ? work.title
              : <span className="text-muted-foreground/50 text-xs">—</span>
          }
        </span>

        {/* Actress */}
        <span className="flex-none w-[140px] text-[12px] text-pink-400/80 truncate hidden md:block">
          {work.actress ?? "—"}
        </span>

        {/* Maker */}
        <span className="flex-none w-[140px] text-[12px] text-muted-foreground truncate hidden lg:block">
          {work.maker ?? "—"}
        </span>

        {/* Label */}
        <span className="flex-none w-[100px] text-[11px] text-muted-foreground/60 truncate hidden xl:block">
          {work.label ?? "—"}
        </span>

        {/* Release date */}
        <span className="flex-none w-[88px] text-[11px] font-mono text-muted-foreground/60 truncate">
          {work.releaseDate ? work.releaseDate.slice(0, 10) : "—"}
        </span>

        {/* Favorite */}
        <button
          onClick={handleFav}
          title={fav ? "お気に入り解除" : "お気に入り"}
          className={`flex-none p-1 rounded-full transition-all
            ${fav
              ? "opacity-100 text-rose-400"
              : "opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-rose-400"
            }`}
        >
          <Heart className="w-3.5 h-3.5" fill={fav ? "currentColor" : "none"} />
        </button>

        {/* Status dot */}
        <span className={`flex-none w-2 h-2 rounded-full ${dotColor}`} title={work.metadataStatus} />

        {/* Delete */}
        <button
          onClick={handleDelete}
          onMouseLeave={() => setConfirm(false)}
          title={confirm ? "もう一度押して確認" : "削除"}
          className={`flex-none opacity-0 group-hover:opacity-100 transition-opacity
            text-[10px] px-1.5 py-0.5 rounded font-bold
            ${confirm
              ? "bg-destructive text-white animate-pulse"
              : "bg-muted text-muted-foreground hover:bg-destructive hover:text-white"
            }`}
        >
          {confirm ? "確認" : <Trash2 className="w-3 h-3" />}
        </button>
      </Link>
    </div>
  );
}
