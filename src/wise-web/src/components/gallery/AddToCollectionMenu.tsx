"use client";

import { useState, useRef, useEffect } from "react";
import { FolderHeart, Plus, Loader2, Check } from "lucide-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchCollections, addWorkToCollection, createCollection } from "@/lib/api";

interface Props {
  workId: string;
}

export function AddToCollectionMenu({ workId }: Props) {
  const [open, setOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState("");
  const [added, setAdded] = useState<string | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const qc = useQueryClient();

  const { data: collections } = useQuery({
    queryKey: ["collections"],
    queryFn: fetchCollections,
    enabled: open,
    staleTime: 30_000,
  });

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  const handleToggle = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setOpen((v) => !v);
  };

  const handleAdd = async (e: React.MouseEvent, collectionId: string, collectionName: string) => {
    e.preventDefault();
    e.stopPropagation();
    await addWorkToCollection(collectionId, workId);
    setAdded(collectionName);
    setTimeout(() => { setAdded(null); setOpen(false); }, 1200);
    await qc.invalidateQueries({ queryKey: ["collections"] });
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!newName.trim()) return;
    const col = await createCollection(newName.trim());
    await addWorkToCollection(col.id, workId);
    await qc.invalidateQueries({ queryKey: ["collections"] });
    setAdded(col.name);
    setNewName("");
    setCreating(false);
    setTimeout(() => { setAdded(null); setOpen(false); }, 1200);
  };

  return (
    <div ref={menuRef} className="relative">
      <button
        onClick={handleToggle}
        title="コレクションに追加"
        className={`p-1 rounded-full backdrop-blur-sm transition-all
          ${open
            ? "opacity-100 bg-black/50 text-primary"
            : "opacity-0 group-hover:opacity-100 bg-black/30 text-white/60 hover:text-primary"
          }`}
      >
        <FolderHeart className="w-3.5 h-3.5" />
      </button>

      {open && (
        <div
          className="absolute right-0 top-full mt-1 z-50 w-44 bg-popover border border-border rounded-xl shadow-xl py-1 text-[12px]"
          onClick={(e) => { e.preventDefault(); e.stopPropagation(); }}
        >
          {added ? (
            <div className="flex items-center gap-1.5 px-3 py-2 text-emerald-400">
              <Check className="w-3.5 h-3.5" />
              <span className="truncate">「{added}」に追加</span>
            </div>
          ) : (
            <>
              {!collections ? (
                <div className="flex justify-center py-3">
                  <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />
                </div>
              ) : collections.length === 0 ? (
                <p className="px-3 py-2 text-muted-foreground">コレクションがありません</p>
              ) : (
                collections.map((c) => (
                  <button
                    key={c.id}
                    onClick={(e) => handleAdd(e, c.id, c.name)}
                    className="w-full text-left px-3 py-1.5 hover:bg-muted/50 transition-colors flex items-center justify-between gap-2"
                  >
                    <span className="truncate">{c.name}</span>
                    <span className="text-[10px] text-muted-foreground/60 shrink-0">{c.itemCount}件</span>
                  </button>
                ))
              )}
              <div className="border-t border-border/40 mt-1 pt-1">
                {creating ? (
                  <form onSubmit={handleCreate} onClick={(e) => e.stopPropagation()} className="px-2 py-1">
                    <input
                      autoFocus
                      type="text"
                      value={newName}
                      onChange={(e) => setNewName(e.target.value)}
                      placeholder="コレクション名"
                      className="w-full h-7 bg-muted/50 rounded-lg px-2 text-[11px] focus:outline-none focus:ring-1 focus:ring-primary/40"
                      onKeyDown={(e) => { if (e.key === "Escape") { setCreating(false); } }}
                    />
                  </form>
                ) : (
                  <button
                    onClick={(e) => { e.preventDefault(); e.stopPropagation(); setCreating(true); }}
                    className="w-full text-left px-3 py-1.5 hover:bg-muted/50 transition-colors flex items-center gap-1.5 text-muted-foreground hover:text-foreground"
                  >
                    <Plus className="w-3 h-3" />
                    新規コレクション
                  </button>
                )}
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
