"use client";

import { useState, useEffect, useCallback, useMemo, useRef, memo } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import {
  ArrowLeft, ChevronUp, ChevronDown, ChevronsUpDown,
  Search, RefreshCw, AlertCircle, Star, Heart, TableProperties,
  Check, RotateCcw, Undo2, Loader2,
} from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { fetchWorks, enqueueFetchMetadataBatch, patchUserData, WorkItem, WorksResponse } from "@/lib/api";

type SortKey = "primaryIdentifier" | "title" | "actress" | "maker" | "metadataStatus" | "rating" | "favorite";
type SortDir = "asc" | "desc";

interface PendingChange {
  rating?: number | null;
  favorite?: boolean;
}

const ROW_HEIGHT = 48;
const FETCH_PAGE_SIZE = 100;
const FETCH_BATCH = 4;

const STATUS_COLORS: Record<string, string> = {
  Organizing:  "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400",
  Failed:      "bg-rose-500/15 text-rose-600 dark:text-rose-400",
  ScanPending: "bg-amber-500/15 text-amber-600 dark:text-amber-400",
};

function statusDot(status: string) {
  const dot =
    status === "Organizing"  ? "bg-emerald-500" :
    status === "Failed"      ? "bg-rose-500" :
    status === "ScanPending" ? "bg-amber-500" : "bg-muted-foreground";
  const cls = STATUS_COLORS[status] ?? "bg-muted text-muted-foreground";
  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-medium ${cls}`}>
      <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${dot}`} />
      {status}
    </span>
  );
}

function SortTh({
  col, label, currentKey, currentDir, onSort, className,
}: {
  col: SortKey; label: string; currentKey: SortKey | null; currentDir: SortDir;
  onSort: (col: SortKey) => void; className?: string;
}) {
  const active = currentKey === col;
  return (
    <th
      className={`px-3 py-3 text-left text-xs font-semibold uppercase tracking-wide cursor-pointer select-none whitespace-nowrap transition-colors ${active ? "text-foreground" : "text-muted-foreground"} hover:text-foreground ${className ?? ""}`}
      onClick={() => onSort(col)}
    >
      <span className="flex items-center gap-1">
        {label}
        {active
          ? currentDir === "asc" ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />
          : <ChevronsUpDown className="w-3.5 h-3.5 opacity-30" />}
      </span>
    </th>
  );
}

// ── Memoized row ──────────────────────────────────────────────────────────────

interface RowProps {
  work: WorkItem;
  hasPending: boolean;
  style: React.CSSProperties;
  onNavigate: (id: string) => void;
  onRating: (id: string, r: number | null) => void;
  onFavorite: (id: string, f: boolean) => void;
}

const OrgRow = memo(function OrgRow({ work, hasPending, style, onNavigate, onRating, onFavorite }: RowProps) {
  return (
    <tr
      style={style}
      className={`flex w-full items-center transition-colors ${hasPending ? "bg-amber-500/5" : "hover:bg-muted/20"}`}
    >
      <td
        className="px-3 flex-none w-36 font-mono font-semibold text-primary whitespace-nowrap cursor-pointer text-xs"
        onClick={() => onNavigate(work.id)}
      >
        {work.primaryIdentifier ?? <span className="text-muted-foreground italic font-normal">—</span>}
        {hasPending && <span className="ml-1 w-1.5 h-1.5 rounded-full bg-amber-500 inline-block align-middle" />}
      </td>
      <td
        className="px-3 flex-1 min-w-0 cursor-pointer text-sm"
        onClick={() => onNavigate(work.id)}
      >
        <span className="block truncate" title={work.title ?? undefined}>
          {work.title ?? <span className="text-muted-foreground italic text-xs">—</span>}
        </span>
      </td>
      <td
        className="px-3 flex-none w-40 text-muted-foreground text-xs truncate cursor-pointer hidden md:block"
        title={work.actress ?? undefined}
        onClick={() => onNavigate(work.id)}
      >
        {work.actress ?? "—"}
      </td>
      <td
        className="px-3 flex-none w-36 text-muted-foreground text-xs truncate cursor-pointer hidden lg:block"
        title={work.maker ?? undefined}
        onClick={() => onNavigate(work.id)}
      >
        {work.maker ?? "—"}
      </td>
      <td className="px-3 flex-none w-36 cursor-pointer hidden sm:block" onClick={() => onNavigate(work.id)}>
        {statusDot(work.metadataStatus)}
      </td>
      {/* Rating — inline */}
      <td className="px-3 flex-none w-32" onClick={e => e.stopPropagation()}>
        <div className="flex gap-0.5">
          {[1, 2, 3, 4, 5].map(star => (
            <button
              key={star}
              onClick={() => onRating(work.id, work.rating === star ? null : star)}
              className={`transition-colors ${(work.rating ?? 0) >= star ? "text-amber-400" : "text-muted-foreground/25 hover:text-amber-300"}`}
            >
              <Star className="w-3.5 h-3.5" fill={(work.rating ?? 0) >= star ? "currentColor" : "none"} />
            </button>
          ))}
        </div>
      </td>
      {/* Favorite — inline */}
      <td className="px-3 flex-none w-20" onClick={e => e.stopPropagation()}>
        <button onClick={() => onFavorite(work.id, !work.favorite)}>
          <Heart
            className={`w-4 h-4 transition-colors ${work.favorite ? "text-rose-500 fill-rose-500" : "text-muted-foreground/30 hover:text-rose-400"}`}
          />
        </button>
      </td>
    </tr>
  );
});

// ── Page ──────────────────────────────────────────────────────────────────────

const STATUS_OPTIONS = ["", "Organizing", "Failed", "ScanPending"] as const;

export default function OrganizePage() {
  const router = useRouter();
  const scrollRef = useRef<HTMLDivElement>(null);

  const [allItems, setAllItems] = useState<WorkItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadedCount, setLoadedCount] = useState(0);
  const [error, setError] = useState<string | null>(null);

  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [sortKey, setSortKey] = useState<SortKey | null>(null);
  const [sortDir, setSortDir] = useState<SortDir>("asc");

  const [isRescanning, setIsRescanning] = useState(false);
  const [rescanResult, setRescanResult] = useState<string | null>(null);

  const [pendingChanges, setPendingChanges] = useState<Record<string, PendingChange>>({});
  const undoStack = useRef<Array<{ workId: string; field: "rating" | "favorite"; oldValue: number | null | boolean }>>([]);
  const [isSaving, setIsSaving] = useState(false);
  const [saveResult, setSaveResult] = useState<string | null>(null);

  const pendingCount = Object.keys(pendingChanges).length;

  // Debounce search
  useEffect(() => {
    const t = setTimeout(() => { setDebouncedQuery(query); }, 350);
    return () => clearTimeout(t);
  }, [query]);

  // Progressive fetch
  const fetchProgressively = useCallback(async (q: string) => {
    setIsLoading(true);
    setAllItems([]);
    setLoadedCount(0);
    setTotalCount(0);
    setError(null);
    try {
      const first: WorksResponse = await fetchWorks(1, FETCH_PAGE_SIZE, q);
      setTotalCount(first.totalCount);
      setAllItems(first.items);
      setLoadedCount(first.items.length);
      setIsLoading(false);

      const maxPages = Math.ceil(first.totalCount / FETCH_PAGE_SIZE);
      if (maxPages > 1) {
        setLoadingMore(true);
        for (let start = 2; start <= maxPages; start += FETCH_BATCH) {
          const end = Math.min(start + FETCH_BATCH - 1, maxPages);
          const results = await Promise.all(
            Array.from({ length: end - start + 1 }, (_, i) =>
              fetchWorks(start + i, FETCH_PAGE_SIZE, q)
            )
          );
          const newItems = results.flatMap(r => r.items);
          setAllItems(prev => [...prev, ...newItems]);
          setLoadedCount(prev => prev + newItems.length);
        }
        setLoadingMore(false);
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Failed to fetch works");
      setIsLoading(false);
      setLoadingMore(false);
    }
  }, []);

  useEffect(() => { fetchProgressively(debouncedQuery); }, [debouncedQuery, fetchProgressively]);

  // Overlay pending changes onto allItems
  const itemsWithPending = useMemo(() =>
    allItems.map(w => {
      const p = pendingChanges[w.id];
      if (!p) return w;
      return {
        ...w,
        rating:   "rating"   in p ? (p.rating ?? null) : w.rating,
        favorite: "favorite" in p ? p.favorite!        : w.favorite,
      };
    }),
    [allItems, pendingChanges]
  );

  const filtered = useMemo(() => {
    let items = itemsWithPending;
    if (statusFilter) items = items.filter(w => w.metadataStatus === statusFilter);
    if (!sortKey) return items;
    return [...items].sort((a, b) => {
      let cmp = 0;
      switch (sortKey) {
        case "primaryIdentifier": cmp = (a.primaryIdentifier ?? "").localeCompare(b.primaryIdentifier ?? "", "ja"); break;
        case "title":       cmp = (a.title    ?? "").localeCompare(b.title    ?? "", "ja"); break;
        case "actress":     cmp = (a.actress  ?? "").localeCompare(b.actress  ?? "", "ja"); break;
        case "maker":       cmp = (a.maker    ?? "").localeCompare(b.maker    ?? "", "ja"); break;
        case "metadataStatus": cmp = a.metadataStatus.localeCompare(b.metadataStatus); break;
        case "rating":   cmp = (a.rating   ?? -1) - (b.rating   ?? -1); break;
        case "favorite": cmp = Number(b.favorite) - Number(a.favorite); break;
      }
      return sortDir === "asc" ? cmp : -cmp;
    });
  }, [itemsWithPending, statusFilter, sortKey, sortDir]);

  const failedItems = allItems.filter(w => w.metadataStatus === "Failed");

  const handleSort = (col: SortKey) => {
    if (sortKey === col) setSortDir(d => d === "asc" ? "desc" : "asc");
    else { setSortKey(col); setSortDir("asc"); }
  };

  const handleRescanFailed = async () => {
    if (failedItems.length === 0) return;
    setIsRescanning(true);
    setRescanResult(null);
    try {
      const result = await enqueueFetchMetadataBatch(failedItems.map(w => w.id));
      setRescanResult(`${result.queued} 件のメタデータ取得をキューに追加しました`);
    } catch (e: unknown) {
      setRescanResult(`エラー: ${e instanceof Error ? e.message : String(e)}`);
    } finally {
      setIsRescanning(false);
    }
  };

  const handleRatingChange = useCallback((workId: string, newRating: number | null) => {
    setAllItems(prev => {
      const original = prev.find(w => w.id === workId);
      const oldRating = pendingChanges[workId]?.rating !== undefined
        ? pendingChanges[workId].rating ?? null
        : original?.rating ?? null;
      undoStack.current.push({ workId, field: "rating", oldValue: oldRating });
      return prev;
    });
    setPendingChanges(prev => ({ ...prev, [workId]: { ...prev[workId], rating: newRating } }));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleFavoriteChange = useCallback((workId: string, newFavorite: boolean) => {
    setAllItems(prev => {
      const original = prev.find(w => w.id === workId);
      const oldFav = pendingChanges[workId]?.favorite !== undefined
        ? pendingChanges[workId].favorite!
        : original?.favorite ?? false;
      undoStack.current.push({ workId, field: "favorite", oldValue: oldFav });
      return prev;
    });
    setPendingChanges(prev => ({ ...prev, [workId]: { ...prev[workId], favorite: newFavorite } }));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleNavigate = useCallback((id: string) => {
    router.push(`/works/${id}`);
  }, [router]);

  const handleUndo = () => {
    const last = undoStack.current.pop();
    if (!last) return;
    setPendingChanges(prev => {
      const next = { ...prev, [last.workId]: { ...prev[last.workId], [last.field]: last.oldValue } };
      const entry = next[last.workId];
      const orig = allItems.find(w => w.id === last.workId);
      const ratingOk   = !("rating"   in entry) || entry.rating   === (orig?.rating   ?? null);
      const favoriteOk = !("favorite" in entry) || entry.favorite === (orig?.favorite ?? false);
      if (ratingOk && favoriteOk) {
        const { [last.workId]: _, ...rest } = next;
        return rest;
      }
      return next;
    });
  };

  const handleRevertAll = () => {
    undoStack.current = [];
    setPendingChanges({});
  };

  const handleCommit = async () => {
    setIsSaving(true);
    setSaveResult(null);
    const entries = Object.entries(pendingChanges);
    let saved = 0, failed = 0;
    await Promise.all(entries.map(async ([workId, change]) => {
      try {
        await patchUserData(workId, {
          ...(change.rating   !== undefined ? { rating:   change.rating   } : {}),
          ...(change.favorite !== undefined ? { favorite: change.favorite } : {}),
        });
        saved++;
      } catch { failed++; }
    }));
    setAllItems(prev => prev.map(w => {
      const p = pendingChanges[w.id];
      if (!p) return w;
      return {
        ...w,
        rating:   "rating"   in p ? (p.rating ?? null) : w.rating,
        favorite: "favorite" in p ? p.favorite!        : w.favorite,
      };
    }));
    undoStack.current = [];
    setPendingChanges({});
    setSaveResult(failed > 0 ? `${saved}件保存、${failed}件失敗` : `${saved}件を保存しました`);
    setIsSaving(false);
    setTimeout(() => setSaveResult(null), 3000);
  };

  // Virtual scroll
  const virtualizer = useVirtualizer({
    count: filtered.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 15,
  });

  return (
    <main className="flex flex-col h-screen bg-background text-foreground overflow-hidden">
      {/* ── Header ── */}
      <header className="shrink-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-14 items-center px-4 md:px-6 gap-3 flex-wrap">
          <Link href="/" className="flex items-center gap-1.5 text-muted-foreground hover:text-foreground transition-colors">
            <ArrowLeft className="w-4 h-4" />
          </Link>
          <div className="flex items-center gap-2 font-bold text-lg">
            <TableProperties className="w-4.5 h-4.5 text-primary" />
            <span>Organize</span>
          </div>

          {/* Load progress */}
          {loadingMore && (
            <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <Loader2 className="w-3 h-3 animate-spin" />
              {loadedCount.toLocaleString()} / {totalCount.toLocaleString()} 件読込中
            </span>
          )}

          <div className="ml-auto flex items-center gap-2 flex-wrap">
            {pendingCount > 0 && (
              <>
                <span className="text-xs text-amber-600 dark:text-amber-400 font-medium bg-amber-500/10 px-2 py-1 rounded-full">
                  {pendingCount}件 変更中
                </span>
                <button
                  onClick={handleUndo}
                  disabled={undoStack.current.length === 0}
                  title="1つ元に戻す"
                  className="h-8 px-3 border rounded-lg text-xs flex items-center gap-1 hover:bg-muted disabled:opacity-40 transition-colors"
                >
                  <Undo2 className="w-3.5 h-3.5" /> Undo
                </button>
                <button
                  onClick={handleRevertAll}
                  className="h-8 px-3 border rounded-lg text-xs flex items-center gap-1 hover:bg-muted transition-colors text-destructive border-destructive/30"
                >
                  <RotateCcw className="w-3.5 h-3.5" /> 全て戻す
                </button>
                <button
                  onClick={handleCommit}
                  disabled={isSaving}
                  className="h-8 px-3 bg-primary text-primary-foreground rounded-lg text-xs font-semibold flex items-center gap-1 hover:bg-primary/90 disabled:opacity-50 transition-colors"
                >
                  {isSaving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                  確定する
                </button>
              </>
            )}
            {saveResult && (
              <span className="text-xs text-emerald-600 dark:text-emerald-400 font-medium">{saveResult}</span>
            )}
            {failedItems.length > 0 && (
              <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-semibold bg-rose-500/15 text-rose-600 dark:text-rose-400">
                <AlertCircle className="w-3.5 h-3.5" /> Failed: {failedItems.length}
              </span>
            )}
            <button
              onClick={handleRescanFailed}
              disabled={isRescanning || failedItems.length === 0}
              className="h-8 px-3 bg-rose-600 text-white font-medium rounded-lg hover:bg-rose-700 disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 text-xs transition-colors"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isRescanning ? "animate-spin" : ""}`} />
              再スキャン（Failed）
            </button>
          </div>
        </div>

        {/* ── Filters ── */}
        <div className="container flex items-center gap-3 px-4 md:px-6 pb-3">
          <div className="relative flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-muted-foreground" />
            <input
              type="text"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="品番・タイトルで検索..."
              className="w-full h-8 bg-muted/50 border rounded-lg pl-8 pr-4 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            />
          </div>
          <select
            value={statusFilter}
            onChange={e => setStatusFilter(e.target.value)}
            className="h-8 bg-background border rounded-lg px-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
          >
            <option value="">全ステータス</option>
            {STATUS_OPTIONS.filter(Boolean).map(s => <option key={s} value={s}>{s}</option>)}
          </select>
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            {isLoading ? "読み込み中..." : `${filtered.length.toLocaleString()} 件`}
          </span>
          {rescanResult && (
            <span className="text-xs text-primary font-medium">{rescanResult}</span>
          )}
          {error && (
            <span className="text-xs text-destructive font-medium">{error}</span>
          )}
        </div>
      </header>

      {/* ── Virtual Table ── */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-auto"
      >
        {isLoading ? (
          <div className="flex items-center justify-center h-40 text-muted-foreground">
            <Loader2 className="w-6 h-6 animate-spin mr-2" /> 読み込み中...
          </div>
        ) : filtered.length === 0 ? (
          <div className="flex items-center justify-center h-40 text-muted-foreground text-sm">
            該当する作品が見つかりませんでした。
          </div>
        ) : (
          <table className="w-full text-sm border-separate border-spacing-0">
            <thead className="sticky top-0 z-10 bg-background/95 backdrop-blur border-b">
              <tr className="flex w-full">
                <SortTh col="primaryIdentifier" label="品番"      currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-36" />
                <SortTh col="title"             label="タイトル"  currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-1 min-w-0" />
                <SortTh col="actress"           label="出演者"    currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-40 hidden md:flex" />
                <SortTh col="maker"             label="メーカー"  currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-36 hidden lg:flex" />
                <SortTh col="metadataStatus"    label="ステータス" currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-36 hidden sm:flex" />
                <SortTh col="rating"            label="評価"      currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-32" />
                <SortTh col="favorite"          label="お気に入り" currentKey={sortKey} currentDir={sortDir} onSort={handleSort} className="flex-none w-20" />
              </tr>
            </thead>
            <tbody
              style={{
                position: "relative",
                display: "block",
                height: `${virtualizer.getTotalSize()}px`,
              }}
            >
              {virtualizer.getVirtualItems().map(vRow => {
                const work = filtered[vRow.index];
                return (
                  <OrgRow
                    key={work.id}
                    work={work}
                    hasPending={!!pendingChanges[work.id]}
                    style={{
                      position: "absolute",
                      top: 0,
                      left: 0,
                      width: "100%",
                      height: `${ROW_HEIGHT}px`,
                      transform: `translateY(${vRow.start}px)`,
                      display: "flex",
                      alignItems: "center",
                      borderBottom: "1px solid hsl(var(--border) / 0.3)",
                    }}
                    onNavigate={handleNavigate}
                    onRating={handleRatingChange}
                    onFavorite={handleFavoriteChange}
                  />
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </main>
  );
}
