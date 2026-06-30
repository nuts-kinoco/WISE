"use client";

import { useEffect, useState } from "react";
import { ArrowLeft, Activity, Image as ImageIcon, Database, FolderPlus, Tag, Trash2, RefreshCw } from "lucide-react";
import Link from "next/link";
import { API_BASE_URL } from "@/lib/api";
import { Skeleton } from "@/components/ui/Skeleton";

interface HistoryDto {
  id: string;
  eventType: string;
  actor: string;
  source: string;
  payload: string | null;
  targetId: string | null;
  targetIdentifier: string | null;
  occurredAt: string;
}

// ── I18n ───────────────────────────────────────────────────────────────────────

const EVENT_LABELS: Record<string, string> = {
  "Work Created":              "インポート完了",
  "Import Completed":          "インポート完了",
  "Identifier Resolved":       "識別子を解決しました",
  "Identifier resolved":       "識別子を解決しました",
  "Metadata Fetched":          "メタデータを取得しました",
  "Metadata fetched":          "メタデータを取得しました",
  "Portrait Cover Downloaded": "ポートレートカバーをダウンロードしました",
  "Landscape Cover Downloaded":"横長カバーをダウンロードしました",
  "Thumbnail Generated":       "サムネイルを生成しました",
  "Thumbnail generated":       "サムネイルを生成しました",
  "Video Ready":               "動画の準備が完了しました",
  "Video ready":               "動画の準備が完了しました",
  "Work Deleted":              "作品を削除しました",
  "Tag Added":                 "タグを追加しました",
  "Tag Removed":               "タグを削除しました",
  "Rating Updated":            "評価を変更しました",
  "Favorite Updated":          "お気に入りを変更しました",
};

function labelOf(eventType: string): string {
  return EVENT_LABELS[eventType] ?? eventType;
}

function iconOf(eventType: string) {
  const t = eventType.toLowerCase();
  if (t.includes("cover") || t.includes("thumbnail") || t.includes("image"))
    return <ImageIcon className="w-4 h-4 text-violet-400" />;
  if (t.includes("metadata"))
    return <Database className="w-4 h-4 text-blue-400" />;
  if (t.includes("import") || t.includes("created"))
    return <FolderPlus className="w-4 h-4 text-emerald-400" />;
  if (t.includes("tag"))
    return <Tag className="w-4 h-4 text-amber-400" />;
  if (t.includes("delete"))
    return <Trash2 className="w-4 h-4 text-rose-400" />;
  if (t.includes("identifier") || t.includes("metadata"))
    return <RefreshCw className="w-4 h-4 text-sky-400" />;
  return <Activity className="w-4 h-4 text-muted-foreground" />;
}

// ── Date helpers ───────────────────────────────────────────────────────────────

function dateGroupKey(iso: string): string {
  return iso.slice(0, 10); // YYYY-MM-DD
}

function formatDateGroup(key: string): string {
  const d = new Date(key);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  const target = new Date(key);

  if (target.getTime() === today.getTime()) return "今日";
  if (target.getTime() === yesterday.getTime()) return "昨日";

  const diffDays = Math.floor((today.getTime() - target.getTime()) / 86400000);
  if (diffDays < 7) return `${diffDays}日前`;

  return d.toLocaleDateString("ja-JP", { year: "numeric", month: "long", day: "numeric" });
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString("ja-JP", { hour: "2-digit", minute: "2-digit" });
}

// ── Component ──────────────────────────────────────────────────────────────────

export default function HistoryPage() {
  const [events, setEvents] = useState<HistoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    fetch(`${API_BASE_URL}/history`)
      .then((r) => r.ok ? r.json() : [])
      .then(setEvents)
      .catch(() => setEvents([]))
      .finally(() => setIsLoading(false));
  }, []);

  // Group by date
  const groups: { key: string; items: HistoryDto[] }[] = [];
  for (const event of events) {
    const key = dateGroupKey(event.occurredAt);
    const last = groups[groups.length - 1];
    if (last && last.key === key) {
      last.items.push(event);
    } else {
      groups.push({ key, items: [event] });
    }
  }

  return (
    <main className="min-h-screen bg-background text-foreground pb-20">
      <header className="sticky top-0 z-50 w-full border-b border-border/50 bg-background/95 backdrop-blur">
        <div className="container flex h-14 items-center gap-3 px-4 md:px-6">
          <Link
            href="/"
            className="flex items-center gap-1.5 text-muted-foreground hover:text-foreground transition-colors text-sm"
          >
            <ArrowLeft className="w-4 h-4" />
            戻る
          </Link>
          <span className="text-border/50">|</span>
          <div className="flex items-center gap-2 font-semibold text-[15px]">
            <Activity className="w-4 h-4 text-primary" />
            システム履歴
          </div>
          {!isLoading && (
            <span className="ml-auto text-xs text-muted-foreground">{events.length}件</span>
          )}
        </div>
      </header>

      <div className="container max-w-2xl mx-auto py-8 px-4 md:px-6">
        {isLoading ? (
          <div className="space-y-8">
            {[3, 5, 4].map((count, gi) => (
              <div key={gi}>
                <Skeleton className="h-4 w-20 mb-4" />
                <div className="relative border-l border-border/40 ml-3 space-y-4 pl-6">
                  {Array.from({ length: count }).map((_, i) => (
                    <div key={i} className="space-y-1">
                      <Skeleton className="h-3.5 w-48" />
                      <Skeleton className="h-3 w-24" />
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        ) : events.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-24 gap-3 text-center">
            <Activity className="w-10 h-10 text-muted-foreground/30" />
            <p className="font-semibold text-foreground">まだ履歴がありません</p>
            <p className="text-sm text-muted-foreground/70">
              作品をインポートするとここに操作履歴が表示されます。
            </p>
          </div>
        ) : (
          <div className="space-y-10">
            {groups.map(({ key, items }) => (
              <section key={key}>
                <h2 className="text-[13px] font-semibold text-muted-foreground mb-3 flex items-center gap-2">
                  {formatDateGroup(key)}
                  <span className="text-[11px] font-normal text-muted-foreground/50">{items.length}件</span>
                </h2>
                <ol className="relative border-l border-border/40 ml-3 space-y-4 pl-6">
                  {items.map((event) => (
                    <li key={event.id} className="relative group">
                      <span className="absolute -left-[25px] top-1.5 w-3 h-3 rounded-full bg-background border-2 border-border/60 flex items-center justify-center group-hover:border-primary/60 transition-colors" />
                      <div className="flex items-start gap-2">
                        <span className="mt-0.5 shrink-0">{iconOf(event.eventType)}</span>
                        <div className="flex-1 min-w-0">
                          <p className="text-[13px] font-medium text-foreground leading-snug">
                            {labelOf(event.eventType)}
                          </p>
                          {event.targetIdentifier && event.targetId && (
                            <Link
                              href={`/works/${event.targetId}`}
                              className="text-[12px] text-primary/80 hover:text-primary transition-colors truncate block"
                            >
                              {event.targetIdentifier}
                            </Link>
                          )}
                        </div>
                        <time className="text-[11px] text-muted-foreground/60 shrink-0 mt-0.5">
                          {formatTime(event.occurredAt)}
                        </time>
                      </div>
                    </li>
                  ))}
                </ol>
              </section>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}
