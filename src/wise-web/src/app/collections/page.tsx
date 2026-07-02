"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import Image from "next/image";
import { ArrowLeft, FolderHeart, Plus, Trash2, Loader2, Image as ImageIcon } from "lucide-react";
import {
  fetchCollections,
  createCollection,
  deleteCollection,
  type CollectionSummary,
} from "@/lib/api";
import { Skeleton } from "@/components/ui/Skeleton";

// ── Collection card ────────────────────────────────────────────────────────────

function CollectionCard({
  collection,
  onDelete,
}: {
  collection: CollectionSummary;
  onDelete: (id: string) => void;
}) {
  return (
    <div className="group relative bg-card border border-border/50 rounded-xl overflow-hidden hover:border-primary/40 transition-colors flex flex-col h-full">
      <Link href={`/collections/${collection.id}`} className="block flex-1">
        <div className="relative w-full h-32 bg-muted/40">
          {collection.coverUrl ? (
            <Image
              src={collection.coverUrl}
              alt={collection.name}
              fill
              className="object-cover transition-transform duration-300 group-hover:scale-105"
              unoptimized
            />
          ) : (
            <div className="absolute inset-0 flex items-center justify-center text-muted-foreground/30">
              <ImageIcon className="w-8 h-8" />
            </div>
          )}
          <div className="absolute inset-0 bg-gradient-to-t from-black/60 to-transparent" />
          <div className="absolute bottom-2 left-3 right-3 flex items-center gap-2">
            <FolderHeart className="w-4 h-4 text-white/90 shrink-0" />
            <h3 className="font-semibold text-[14px] text-white truncate drop-shadow-md">{collection.name}</h3>
          </div>
        </div>

        <div className="p-4 flex flex-col justify-between flex-1">
          {collection.description && (
            <p className="text-[12px] text-muted-foreground line-clamp-2 mb-2">{collection.description}</p>
          )}
          <p className="text-[12px] text-muted-foreground/60">
            {collection.itemCount}件
          </p>
        </div>
      </Link>
      <button
        onClick={() => onDelete(collection.id)}
        className="absolute top-3 right-3 opacity-0 group-hover:opacity-100 p-1.5 rounded-lg text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-all"
        title="削除"
      >
        <Trash2 className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}

// ── Create dialog ──────────────────────────────────────────────────────────────

function CreateDialog({ onClose, onCreate }: { onClose: () => void; onCreate: (name: string, desc: string) => Promise<void> }) {
  const [name, setName] = useState("");
  const [desc, setDesc] = useState("");
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    setLoading(true);
    try {
      await onCreate(name.trim(), desc.trim());
      onClose();
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border border-border rounded-2xl p-6 w-full max-w-md shadow-xl">
        <h2 className="text-[15px] font-semibold mb-4">新しいコレクション</h2>
        <form onSubmit={handleSubmit} className="space-y-3">
          <input
            autoFocus
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="コレクション名"
            className="w-full h-10 bg-muted/40 rounded-xl px-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            required
          />
          <textarea
            value={desc}
            onChange={(e) => setDesc(e.target.value)}
            placeholder="説明（任意）"
            rows={2}
            className="w-full bg-muted/40 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40 resize-none"
          />
          <div className="flex gap-2 justify-end pt-1">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 rounded-xl text-sm text-muted-foreground hover:text-foreground border border-border/50 hover:border-border transition-colors"
            >
              キャンセル
            </button>
            <button
              type="submit"
              disabled={!name.trim() || loading}
              className="px-4 py-2 rounded-xl text-sm bg-primary text-primary-foreground font-medium hover:opacity-90 disabled:opacity-50 transition-opacity"
            >
              {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : "作成"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function CollectionsPage() {
  const qc = useQueryClient();
  const [showCreate, setShowCreate] = useState(false);

  const { data: collections, isLoading } = useQuery({
    queryKey: ["collections"],
    queryFn: fetchCollections,
  });

  const handleCreate = async (name: string, description: string) => {
    await createCollection(name, description || undefined);
    await qc.invalidateQueries({ queryKey: ["collections"] });
  };

  const handleDelete = async (id: string) => {
    await deleteCollection(id);
    await qc.invalidateQueries({ queryKey: ["collections"] });
  };

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
            <FolderHeart className="w-4 h-4 text-primary" />
            コレクション
          </div>
          <button
            onClick={() => setShowCreate(true)}
            className="ml-auto flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-primary text-primary-foreground text-[12px] font-medium hover:opacity-90 transition-opacity"
          >
            <Plus className="w-3.5 h-3.5" />
            新規作成
          </button>
        </div>
      </header>

      <div className="container max-w-4xl mx-auto py-8 px-4 md:px-6">
        {isLoading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="rounded-xl border border-border/50 p-4">
                <Skeleton className="h-4 w-32 mb-2" />
                <Skeleton className="h-3 w-full mb-1" />
                <Skeleton className="h-3 w-16 mt-2" />
              </div>
            ))}
          </div>
        ) : !collections || collections.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-24 gap-3 text-center">
            <FolderHeart className="w-10 h-10 text-muted-foreground/30" />
            <p className="font-semibold text-foreground">コレクションがありません</p>
            <p className="text-sm text-muted-foreground/70 max-w-xs">
              作品をグループ化するコレクションを作成してみましょう。
            </p>
            <button
              onClick={() => setShowCreate(true)}
              className="mt-2 px-5 py-2 rounded-xl bg-primary text-primary-foreground text-[13px] font-medium hover:opacity-90 transition-opacity"
            >
              最初のコレクションを作成
            </button>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
            {collections.map((c) => (
              <CollectionCard key={c.id} collection={c} onDelete={handleDelete} />
            ))}
          </div>
        )}
      </div>

      {showCreate && (
        <CreateDialog onClose={() => setShowCreate(false)} onCreate={handleCreate} />
      )}
    </main>
  );
}
