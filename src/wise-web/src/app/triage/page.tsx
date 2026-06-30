"use client";

import { API_BASE_URL, enqueueFetchMetadataBatch } from "@/lib/api";
import { ArrowLeft, Save, RefreshCw, AlertTriangle } from "lucide-react";
import Link from "next/link";
import { useEffect, useState, useCallback } from "react";

interface TriageWork {
  id: string;
  primaryIdentifier: string | null;
  metadataStatus: string;
  // editable fields
  title: string;
  actress: string;
  maker: string;
  label: string;
  series: string;
  releaseDate: string;
  genre: string;
  runtime: string;
  // dirty tracking
  dirty: boolean;
  saving: boolean;
  saved: boolean;
}

const STATUS_COLORS: Record<string, string> = {
  Failed: "text-destructive",
  NotFound: "text-amber-400",
  NetworkError: "text-orange-400",
  MetadataPending: "text-muted-foreground",
  ScanPending: "text-muted-foreground",
};

export default function TriagePage() {
  const [works, setWorks] = useState<TriageWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [q, setQ] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [batchScraping, setBatchScraping] = useState(false);
  const [batchMsg, setBatchMsg] = useState<string | null>(null);
  const PAGE_SIZE = 50;

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const url = new URL(`${API_BASE_URL}/works`);
      url.searchParams.set("page", String(page));
      url.searchParams.set("pageSize", String(PAGE_SIZE));
      url.searchParams.set("status", "Failed,NotFound,NetworkError,MetadataPending,ScanPending");
      if (q) url.searchParams.set("q", q);
      const res = await fetch(url.toString());
      const data = await res.json();
      setTotalCount(data.totalCount);
      setWorks(
        data.items.map((w: {
          id: string;
          primaryIdentifier: string | null;
          metadataStatus: string;
          title: string | null;
          actress: string | null;
          maker: string | null;
          label: string | null;
          releaseDate: string | null;
        }) => ({
          id: w.id,
          primaryIdentifier: w.primaryIdentifier,
          metadataStatus: w.metadataStatus,
          title: w.title ?? "",
          actress: w.actress ?? "",
          maker: w.maker ?? "",
          label: w.label ?? "",
          series: "",
          releaseDate: w.releaseDate ?? "",
          genre: "",
          runtime: "",
          dirty: false,
          saving: false,
          saved: false,
        }))
      );
    } finally {
      setLoading(false);
    }
  }, [page, q]);

  useEffect(() => { load(); setSelected(new Set()); }, [load]);

  const toggleSelect = (id: string) => setSelected(prev => {
    const next = new Set(prev);
    next.has(id) ? next.delete(id) : next.add(id);
    return next;
  });

  const toggleAll = () => setSelected(prev =>
    prev.size === works.length ? new Set() : new Set(works.map(w => w.id))
  );

  const handleBatchRescrape = async () => {
    if (selected.size === 0 || batchScraping) return;
    setBatchScraping(true);
    setBatchMsg(null);
    try {
      const result = await enqueueFetchMetadataBatch(Array.from(selected));
      setBatchMsg(`${result.queued}件をキューに追加しました`);
      setSelected(new Set());
    } catch {
      setBatchMsg("エラーが発生しました");
    } finally {
      setBatchScraping(false);
      setTimeout(() => setBatchMsg(null), 3000);
    }
  };

  const updateField = (id: string, field: keyof TriageWork, value: string) => {
    setWorks(prev => prev.map(w =>
      w.id === id ? { ...w, [field]: value, dirty: true, saved: false } : w
    ));
  };

  const save = async (work: TriageWork) => {
    setWorks(prev => prev.map(w => w.id === work.id ? { ...w, saving: true } : w));
    try {
      const res = await fetch(`${API_BASE_URL}/works/${work.id}/metadata`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          title: work.title || null,
          actress: work.actress || null,
          maker: work.maker || null,
          label: work.label || null,
          series: work.series || null,
          releaseDate: work.releaseDate || null,
          genre: work.genre || null,
          runtime: work.runtime || null,
        }),
      });
      if (!res.ok) throw new Error("save failed");
      setWorks(prev => prev.map(w =>
        w.id === work.id ? { ...w, saving: false, dirty: false, saved: true } : w
      ));
    } catch {
      setWorks(prev => prev.map(w => w.id === work.id ? { ...w, saving: false } : w));
    }
  };

  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  return (
    <main className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-14 items-center gap-4 px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-semibold">
            <AlertTriangle className="w-4 h-4 text-amber-400" />
            <span>トリアージ</span>
            {totalCount > 0 && (
              <span className="text-xs bg-destructive/20 text-destructive px-2 py-0.5 rounded-full font-mono">{totalCount}</span>
            )}
          </div>
          <input
            type="text"
            placeholder="識別子・タイトルで絞り込み..."
            value={q}
            onChange={e => { setQ(e.target.value); setPage(1); }}
            className="ml-auto w-56 bg-muted/40 border border-border/50 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-primary/50"
          />
          {batchMsg && <span className="text-xs text-muted-foreground">{batchMsg}</span>}
          {selected.size > 0 && (
            <button
              onClick={handleBatchRescrape}
              disabled={batchScraping}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-primary/15 text-primary hover:bg-primary/25 text-xs font-medium transition-colors"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${batchScraping ? "animate-spin" : ""}`} />
              {selected.size}件を再スキャン
            </button>
          )}
          <button onClick={load} className="p-1.5 rounded-lg hover:bg-muted/50 transition-colors text-muted-foreground hover:text-foreground">
            <RefreshCw className="w-4 h-4" />
          </button>
        </div>
      </header>

      <div className="container mx-auto px-2 md:px-4 py-4">
        {loading ? (
          <div className="flex items-center justify-center py-20 text-muted-foreground text-sm">読み込み中...</div>
        ) : works.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 gap-3 text-muted-foreground">
            <AlertTriangle className="w-12 h-12 opacity-30" />
            <p>トリアージ対象の作品はありません</p>
          </div>
        ) : (
          <>
            <div className="overflow-x-auto rounded-xl border border-border/50">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border/50 bg-muted/30">
                    <th className="px-3 py-2.5 w-8">
                      <input type="checkbox" checked={selected.size === works.length && works.length > 0} onChange={toggleAll} className="w-3.5 h-3.5 accent-primary cursor-pointer" />
                    </th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-32">識別子</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-20">状態</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground min-w-[180px]">タイトル</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-28">出演者</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-28">メーカー</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-24">レーベル</th>
                    <th className="text-left px-3 py-2.5 font-medium text-muted-foreground w-24">発売日</th>
                    <th className="px-3 py-2.5 w-16"></th>
                  </tr>
                </thead>
                <tbody>
                  {works.map((work, i) => (
                    <tr key={work.id} className={`border-b border-border/30 hover:bg-muted/20 transition-colors ${selected.has(work.id) ? "bg-primary/5" : i % 2 === 0 ? "" : "bg-muted/5"}`}>
                      <td className="px-3 py-1.5">
                        <input type="checkbox" checked={selected.has(work.id)} onChange={() => toggleSelect(work.id)} className="w-3.5 h-3.5 accent-primary cursor-pointer" />
                      </td>
                      <td className="px-3 py-1.5">
                        <Link href={`/works/${work.id}`} className="font-mono text-xs text-primary hover:underline">
                          {work.primaryIdentifier}
                        </Link>
                      </td>
                      <td className="px-3 py-1.5">
                        <span className={`text-xs font-medium ${STATUS_COLORS[work.metadataStatus] ?? "text-muted-foreground"}`}>
                          {work.metadataStatus}
                        </span>
                      </td>
                      <td className="px-3 py-1.5">
                        <input
                          type="text"
                          value={work.title}
                          onChange={e => updateField(work.id, "title", e.target.value)}
                          placeholder="タイトルを入力..."
                          className="w-full bg-transparent border-b border-transparent hover:border-border focus:border-primary focus:outline-none py-0.5 text-sm transition-colors placeholder:text-muted-foreground/40"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        <input
                          type="text"
                          value={work.actress}
                          onChange={e => updateField(work.id, "actress", e.target.value)}
                          placeholder="—"
                          className="w-full bg-transparent border-b border-transparent hover:border-border focus:border-primary focus:outline-none py-0.5 text-sm transition-colors placeholder:text-muted-foreground/40"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        <input
                          type="text"
                          value={work.maker}
                          onChange={e => updateField(work.id, "maker", e.target.value)}
                          placeholder="—"
                          className="w-full bg-transparent border-b border-transparent hover:border-border focus:border-primary focus:outline-none py-0.5 text-sm transition-colors placeholder:text-muted-foreground/40"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        <input
                          type="text"
                          value={work.label}
                          onChange={e => updateField(work.id, "label", e.target.value)}
                          placeholder="—"
                          className="w-full bg-transparent border-b border-transparent hover:border-border focus:border-primary focus:outline-none py-0.5 text-sm transition-colors placeholder:text-muted-foreground/40"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        <input
                          type="text"
                          value={work.releaseDate}
                          onChange={e => updateField(work.id, "releaseDate", e.target.value)}
                          placeholder="YYYY/MM/DD"
                          className="w-full bg-transparent border-b border-transparent hover:border-border focus:border-primary focus:outline-none py-0.5 text-sm transition-colors placeholder:text-muted-foreground/40"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        {work.saved ? (
                          <span className="text-xs text-emerald-400 font-medium">✓</span>
                        ) : (
                          <button
                            onClick={() => save(work)}
                            disabled={!work.dirty || work.saving}
                            className={`flex items-center gap-1 px-2 py-1 rounded text-xs font-medium transition-colors ${
                              work.dirty
                                ? "bg-primary/15 text-primary hover:bg-primary/25"
                                : "text-muted-foreground/30 cursor-default"
                            }`}
                          >
                            {work.saving ? (
                              <RefreshCw className="w-3 h-3 animate-spin" />
                            ) : (
                              <Save className="w-3 h-3" />
                            )}
                            保存
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {totalPages > 1 && (
              <div className="flex items-center justify-center gap-2 mt-4 text-sm">
                <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
                  className="px-3 py-1.5 rounded-lg border border-border/50 disabled:opacity-40 hover:bg-muted/50 transition-colors">
                  前へ
                </button>
                <span className="text-muted-foreground">{page} / {totalPages}</span>
                <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}
                  className="px-3 py-1.5 rounded-lg border border-border/50 disabled:opacity-40 hover:bg-muted/50 transition-colors">
                  次へ
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </main>
  );
}
