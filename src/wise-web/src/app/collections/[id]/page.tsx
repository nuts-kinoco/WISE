"use client";

import { use } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, FolderHeart, X } from "lucide-react";
import { fetchCollection, removeWorkFromCollection } from "@/lib/api";
import { DashboardCard } from "@/components/dashboard/DashboardCard";
import { Skeleton } from "@/components/ui/Skeleton";

export default function CollectionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const qc = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["collection", id],
    queryFn: () => fetchCollection(id),
  });

  const handleRemove = async (workId: string) => {
    await removeWorkFromCollection(id, workId);
    await qc.invalidateQueries({ queryKey: ["collection", id] });
    await qc.invalidateQueries({ queryKey: ["collections"] });
  };

  return (
    <main className="min-h-screen bg-background text-foreground pb-20">
      <header className="sticky top-0 z-50 w-full border-b border-border/50 bg-background/95 backdrop-blur">
        <div className="container flex h-14 items-center gap-3 px-4 md:px-6">
          <Link
            href="/collections"
            className="flex items-center gap-1.5 text-muted-foreground hover:text-foreground transition-colors text-sm"
          >
            <ArrowLeft className="w-4 h-4" />
            コレクション一覧
          </Link>
          <span className="text-border/50">|</span>
          {isLoading ? (
            <Skeleton className="h-4 w-32" />
          ) : (
            <div className="flex items-center gap-2 font-semibold text-[15px]">
              <FolderHeart className="w-4 h-4 text-primary" />
              {data?.name}
            </div>
          )}
          {!isLoading && data && (
            <span className="ml-auto text-xs text-muted-foreground">{data.items.length}件</span>
          )}
        </div>
      </header>

      <div className="container max-w-screen-xl mx-auto py-8 px-4 md:px-6">
        {isLoading ? (
          <div className="flex flex-wrap gap-4">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="w-36">
                <Skeleton className="w-full aspect-[2/3] rounded-lg" />
                <div className="mt-1.5 space-y-1">
                  <Skeleton className="h-3 w-full" />
                  <Skeleton className="h-3 w-2/3" />
                </div>
              </div>
            ))}
          </div>
        ) : !data || data.items.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-24 gap-3 text-center">
            <FolderHeart className="w-10 h-10 text-muted-foreground/30" />
            <p className="font-semibold text-foreground">このコレクションは空です</p>
            <p className="text-sm text-muted-foreground/70 max-w-xs">
              作品ページのカードメニューからコレクションに追加できます。
            </p>
          </div>
        ) : (
          <div className="flex flex-wrap gap-4">
            {data.items.map((item) => (
              <div key={item.id} className="relative group">
                <DashboardCard work={item.work} />
                <button
                  onClick={() => handleRemove(item.work.id)}
                  className="absolute top-1.5 right-1.5 opacity-0 group-hover:opacity-100 w-6 h-6 rounded-full bg-black/60 text-white flex items-center justify-center hover:bg-black/80 transition-all"
                  title="コレクションから削除"
                >
                  <X className="w-3 h-3" />
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}
