"use client";

import { useCallback, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Shuffle, Loader2, Library } from "lucide-react";
import { fetchHome, fetchRandomWork, type WorkItem } from "@/lib/api";
import { useDeviceId } from "@/hooks/useDeviceId";
import { DashboardCard } from "./DashboardCard";

// ── Widget row ─────────────────────────────────────────────────────────────

function WidgetRow({
  title,
  works,
  badge,
  viewAllHref,
}: {
  title: string;
  works: WorkItem[];
  badge?: string;
  viewAllHref?: string;
}) {
  if (works.length === 0) return null;

  return (
    <section className="mb-8">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-[15px] font-semibold text-foreground">{title}</h2>
        {viewAllHref && (
          <a
            href={viewAllHref}
            className="text-[12px] text-muted-foreground hover:text-primary transition-colors"
          >
            すべて見る →
          </a>
        )}
      </div>
      <div className="flex gap-3 overflow-x-auto pb-2 scrollbar-none">
        {works.map((w) => (
          <DashboardCard key={w.id} work={w} badge={badge} />
        ))}
      </div>
    </section>
  );
}

// ── Random Pick ────────────────────────────────────────────────────────────

function RandomPickWidget() {
  const router = useRouter();
  const qc = useQueryClient();
  const [loading, setLoading] = useState(false);

  const pick = useCallback(async () => {
    setLoading(true);
    try {
      const work = await fetchRandomWork();
      if (work) router.push(`/works/${work.id}`);
    } finally {
      setLoading(false);
    }
  }, [router]);

  return (
    <section className="mb-8">
      <h2 className="text-[15px] font-semibold text-foreground mb-3">今日の一本</h2>
      <button
        onClick={pick}
        disabled={loading}
        className="flex items-center gap-2 px-5 py-2.5 rounded-xl border border-border/60
          text-[13px] font-medium text-muted-foreground hover:text-foreground hover:border-border
          hover:bg-muted/40 transition-all disabled:opacity-50"
      >
        {loading
          ? <Loader2 className="w-4 h-4 animate-spin" />
          : <Shuffle className="w-4 h-4" />
        }
        ランダムに選ぶ
      </button>
    </section>
  );
}

// ── Empty state ────────────────────────────────────────────────────────────

function DashboardEmpty({ onSwitchToLibrary }: { onSwitchToLibrary: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center py-24 gap-4 text-center">
      <Library className="w-12 h-12 text-muted-foreground/30" />
      <p className="text-lg font-medium text-muted-foreground">ライブラリが空です</p>
      <p className="text-sm text-muted-foreground/60 max-w-xs">
        作品をインポートすると、ここに表示されます
      </p>
      <a
        href="/import"
        className="mt-2 px-5 py-2 rounded-xl bg-primary text-primary-foreground text-[13px] font-medium
          hover:opacity-90 transition-opacity"
      >
        作品をインポートする
      </a>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────

export function DashboardView({ onSwitchToLibrary }: { onSwitchToLibrary: () => void }) {
  const deviceId = useDeviceId();

  const { data, isLoading, isError } = useQuery({
    queryKey: ["home", deviceId],
    queryFn: () => fetchHome(deviceId),
    staleTime: 60_000,
    enabled: !!deviceId,
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24">
        <Loader2 className="w-6 h-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-2 text-muted-foreground">
        <p className="text-sm">ダッシュボードを読み込めませんでした</p>
        <p className="text-xs text-muted-foreground/60">APIサーバーが起動しているか確認してください</p>
      </div>
    );
  }

  const hasAny =
    data.continueWatching.length > 0 ||
    data.recentlyAdded.length > 0 ||
    data.favorites.length > 0;

  if (!hasAny) {
    return <DashboardEmpty onSwitchToLibrary={onSwitchToLibrary} />;
  }

  return (
    <div className="w-full max-w-screen-2xl mx-auto px-4 md:px-6 py-6">
      <WidgetRow
        title="続きを見る"
        works={data.continueWatching}
        badge="続きから"
      />
      <WidgetRow
        title="最近追加された作品"
        works={data.recentlyAdded}
        viewAllHref="/?view=library"
      />
      <WidgetRow
        title="お気に入り"
        works={data.favorites}
      />
      <RandomPickWidget />
    </div>
  );
}
