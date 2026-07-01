"use client";

import Image from "next/image";
import Link from "next/link";
import { Heart, Star } from "lucide-react";
import { patchUserData, type WorkItem } from "@/lib/api";
import { useState } from "react";
import { useGalleryStore, type DisplayField } from "@/store/useGalleryStore";
import { resolveCoverUrl } from "@/lib/media";
import { AddToCollectionMenu } from "./AddToCollectionMenu";

interface Props {
  work: WorkItem;
  style?: React.CSSProperties;
}

function RatingStars({ rating, size = "sm" }: { rating: number | null; size?: "sm" | "xs" }) {
  if (!rating) return null;
  const cls = size === "xs" ? "w-2 h-2" : "w-2.5 h-2.5";
  return (
    <div className="flex gap-px flex-none">
      {[1, 2, 3, 4, 5].map((i) => (
        <Star
          key={i}
          className={`${cls} ${i <= rating ? "text-amber-400 fill-amber-400" : "text-muted-foreground/20"}`}
        />
      ))}
    </div>
  );
}

const STATUS_DOT: Record<string, string> = {
  Organizing:      "bg-emerald-500",
  Failed:          "bg-rose-500",
  ScanPending:     "bg-amber-400",
  MetadataFetching:"bg-blue-400",
};

export function WorkCard({ work, style }: Props) {
  const { density, coverLayout, displayFields } = useGalleryStore();
  const [fav, setFav] = useState(work.favorite);

  const show = (f: DisplayField) => displayFields[f];

  const rawCover = coverLayout === "landscape"
    ? (work.coverLandscapeUrl ?? work.coverUrl)
    : work.coverUrl;
  const src = resolveCoverUrl(rawCover);
  const isLandscape = coverLayout === "landscape";

  const isComicType = work.mediaType === "Comic" || work.mediaType === "Book"
    || work.mediaType === "PhotoBook" || work.mediaType === "ImageCollection";

  // Work state helpers
  const hasData = !!(work.title || work.actress || work.author);
  const isPending = !hasData
    && work.metadataStatus !== "Failed"
    && work.metadataStatus !== "NotFound"
    && work.metadataStatus !== "Organizing";

  const statusDot = STATUS_DOT[work.metadataStatus] ?? "bg-muted-foreground/30";
  const isCompact = density === "compact";
  const isRich = density === "rich";

  const handleFav = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const next = !fav;
    setFav(next);
    try { await patchUserData(work.id, { favorite: next, rating: work.rating }); }
    catch { setFav(!next); }
  };

  return (
    <Link
      href={`/works/${work.id}`}
      style={style}
      className={`block ${isCompact ? "p-1" : "p-1.5"}`}
    >
      <article
        className={`relative flex flex-col overflow-hidden bg-card group
          transition-all duration-200 hover:ring-1 hover:ring-primary/20 hover:shadow-xl
          ${isCompact ? "rounded-lg" : "rounded-xl shadow-sm"}`}
      >
        {/* ── Cover image ── */}
        <div
          className={`relative w-full flex-none overflow-hidden bg-muted/30
            ${isLandscape ? "aspect-video" : "aspect-[2/3]"}`}
        >
          {src ? (
            <Image
              src={src}
              alt={work.title ?? work.primaryIdentifier ?? ""}
              fill
              sizes="(max-width: 640px) 50vw, (max-width: 1280px) 25vw, 16vw"
              className="object-cover transition-transform duration-500 group-hover:scale-[1.04]"
              loading="lazy"
              unoptimized={src.startsWith("http://localhost")}
            />
          ) : isPending ? (
            <div className="absolute inset-0 bg-gradient-to-br from-muted to-muted/40 animate-pulse" />
          ) : (
            /* no cover, show identifier as placeholder */
            <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-br from-slate-800/70 to-slate-900/90">
              <span className="text-[9px] font-mono text-slate-400/70 px-2 text-center break-all leading-relaxed">
                {work.primaryIdentifier}
              </span>
            </div>
          )}

          {/* Compact / Normal hover overlay — title/actress over a gradient */}
          {!isRich && (
            <div className={`absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/80 to-transparent
              opacity-0 group-hover:opacity-100 transition-opacity duration-200
              flex flex-col justify-end pointer-events-none
              ${isCompact ? "h-2/5 px-2 pb-2" : "h-1/3 px-3 pb-3"}`}
            >
              {work.title && (
                <p className={`text-white font-medium line-clamp-2 leading-tight drop-shadow
                  ${isCompact ? "text-[10px]" : "text-[11px]"}`}
                >
                  {work.title}
                </p>
              )}
              {isComicType
                ? show("actress") && work.author && (
                    <p className={`text-sky-300/90 line-clamp-1 mt-0.5 drop-shadow
                      ${isCompact ? "text-[9px]" : "text-[10px]"}`}
                    >
                      {work.author}{work.circle ? ` / ${work.circle}` : ""}
                    </p>
                  )
                : show("actress") && work.actress && (
                    <p className={`text-pink-300/90 line-clamp-1 mt-0.5 drop-shadow
                      ${isCompact ? "text-[9px]" : "text-[10px]"}`}
                    >
                      {work.actress}
                    </p>
                  )
              }
            </div>
          )}

          {/* Favorite button — always top-left */}
          {show("favorite") && (
            <button
              onClick={handleFav}
              title={fav ? "お気に入り解除" : "お気に入り追加"}
              className={`absolute top-1.5 left-1.5 p-1 rounded-full backdrop-blur-sm transition-all
                ${fav
                  ? "opacity-100 bg-black/50 text-rose-400"
                  : "opacity-0 group-hover:opacity-100 bg-black/30 text-white/60 hover:text-rose-400"
                }`}
            >
              <Heart className="w-3.5 h-3.5" fill={fav ? "currentColor" : "none"} />
            </button>
          )}

          {/* Collection + Status — top-right corner */}
          <div className="absolute top-1.5 right-1.5 flex items-center gap-1">
            <AddToCollectionMenu workId={work.id} />
            {show("status") && (
              <span
                className={`w-2 h-2 rounded-full shadow ${statusDot}`}
                title={work.metadataStatus}
              />
            )}
          </div>
        </div>

        {/* ── Info strip (Rich only) ── */}
        {isRich && (
          <div className={`flex flex-col min-w-0 px-2.5 ${isRich ? "py-2.5 gap-1.5" : "py-2 gap-0.5"}`}>

            {/* Row 1: identifier + inline status dot + rating (normal) */}
            <div className="flex items-center gap-1.5 min-w-0">
              {show("identifier") && (
                <span className="text-[10px] font-mono text-primary/60 uppercase tracking-wider truncate flex-1 min-w-0">
                  {work.primaryIdentifier ?? "—"}
                </span>
              )}
              {/* Subtle status dot when not shown as overlay */}
              {!show("status") && (
                <span className={`w-1.5 h-1.5 rounded-full flex-none ${statusDot}`} title={work.metadataStatus} />
              )}
              {show("rating") && !isRich && <RatingStars rating={work.rating} size="xs" />}
            </div>

            {/* Title */}
            {show("title") && (
              <p className={`font-semibold text-foreground leading-snug min-w-0
                ${isRich ? "text-[13px] line-clamp-3" : "text-[11px] line-clamp-2"}`}
              >
                {isPending
                  ? <span className="block h-3 w-full rounded bg-muted/60 animate-pulse" />
                  : work.title
                    ? work.title
                    : <span className="text-muted-foreground/40 font-normal text-[10px] italic">タイトル未取得</span>
                }
              </p>
            )}

            {/* Actress / Author */}
            {show("actress") && (
              <p className={`text-[11px] truncate min-w-0 ${isComicType ? "text-sky-400/80" : "text-pink-400/80"}`}>
                {isPending
                  ? <span className="block h-2.5 w-2/3 rounded bg-muted/50 animate-pulse" />
                  : isComicType
                    ? (work.author ?? null)
                    : (work.actress ?? null)
                }
              </p>
            )}

            {/* Rich-only additional fields */}
            {isRich && (
              <>
                {isComicType
                  ? (
                    <>
                      {show("maker") && work.circle && (
                        <p className="text-[11px] text-muted-foreground truncate">{work.circle}</p>
                      )}
                      {work.pageCount && (
                        <p className="text-[10px] text-muted-foreground/60 truncate">{work.pageCount}ページ</p>
                      )}
                    </>
                  ) : (
                    <>
                      {show("maker") && work.maker && (
                        <p className="text-[11px] text-muted-foreground truncate">{work.maker}</p>
                      )}
                      {show("label") && work.label && (
                        <p className="text-[10px] text-muted-foreground/60 truncate">{work.label}</p>
                      )}
                    </>
                  )
                }
                <div className="flex items-center justify-between mt-0.5 min-w-0">
                  {show("releaseDate") && work.releaseDate && (
                    <span className="text-[10px] font-mono text-muted-foreground/50">
                      {work.releaseDate.slice(0, 10)}
                    </span>
                  )}
                  {show("rating") && <RatingStars rating={work.rating} />}
                </div>
              </>
            )}
          </div>
        )}
      </article>
    </Link>
  );
}
