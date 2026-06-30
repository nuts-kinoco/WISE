"use client";

import { useGalleryStore, type Density, type SortOption } from "@/store/useGalleryStore";
import type { MediaType } from "@/lib/api";
import { GalleryGrid } from "@/components/gallery/GalleryGrid";
import { DisplaySettingsPanel } from "@/components/gallery/DisplaySettingsPanel";
import { DashboardView } from "@/components/dashboard/DashboardView";
import {
  Search, Moon, Sun, Monitor, Settings2, HardDriveUpload,
  Clock, Activity, RectangleVertical, RectangleHorizontal,
  X, AlertTriangle, TableProperties, Copy, SlidersHorizontal,
  List, Grid, LayoutGrid, Rows3, Home as HomeIcon, BookImage, FolderHeart,
} from "lucide-react";
import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { JobQueuePanel, TopProgressBar } from "@/components/JobQueuePanel";

type MainView = "home" | "library";

// ── Density mode config ───────────────────────────────────────────────────────

const DENSITY_OPTIONS: { value: Density; icon: React.ReactNode; label: string }[] = [
  { value: "compact", icon: <Grid className="w-4 h-4" />,       label: "コンパクト" },
  { value: "normal",  icon: <LayoutGrid className="w-4 h-4" />, label: "ノーマル"   },
  { value: "rich",    icon: <Rows3 className="w-4 h-4" />,      label: "リッチ"     },
  { value: "list",    icon: <List className="w-4 h-4" />,       label: "リスト"     },
];

// ── MediaType filter tabs ─────────────────────────────────────────────────────

const MEDIA_TYPE_TABS: { value: MediaType | null; label: string }[] = [
  { value: null,              label: "すべて" },
  { value: "Video",           label: "動画" },
  { value: "Comic",           label: "コミック" },
  { value: "Book",            label: "書籍" },
  { value: "PhotoBook",       label: "写真集" },
  { value: "ImageCollection", label: "画像集" },
  { value: "Audio",           label: "音声" },
];

// ── Sort options ──────────────────────────────────────────────────────────────

const SORT_OPTIONS: { value: SortOption; label: string }[] = [
  { value: "added",      label: "追加日（新しい順）" },
  { value: "rating",     label: "評価順" },
  { value: "title",      label: "タイトル順" },
  { value: "identifier", label: "品番順" },
  { value: "release",    label: "発売日（新しい順）" },
  { value: "random",     label: "ランダム" },
];

// ── Component ─────────────────────────────────────────────────────────────────

export default function Home() {
  const {
    searchQuery, setSearchQuery,
    density, setDensity,
    theme, setTheme,
    coverLayout, setCoverLayout,
    mediaTypeFilter, setMediaTypeFilter,
    sort, setSort,
  } = useGalleryStore();

  const [mounted, setMounted] = useState(false);
  const [showDisplayPanel, setShowDisplayPanel] = useState(false);
  const [mainView, setMainView] = useState<MainView>("home");

  useEffect(() => setMounted(true), []);

  const closePanel = useCallback(() => setShowDisplayPanel(false), []);

  return (
    <main className="flex min-h-screen flex-col bg-background">
      <TopProgressBar />

      {/* ── Header ── */}
      <header className="sticky top-0 z-50 w-full border-b border-border/50 bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center gap-3 px-4 md:px-6">

          {/* Logo */}
          <Link
            href="/"
            className="flex items-center gap-2 font-bold text-xl tracking-wider hover:opacity-80 transition-opacity flex-none"
          >
            <span className="text-primary">WISE</span>
          </Link>

          {/* Home / Library tab switcher */}
          {mounted && (
            <div className="flex items-center gap-0.5 bg-muted/40 rounded-xl p-1 mr-2 flex-none">
              <button
                onClick={() => setMainView("home")}
                title="ホーム"
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[13px] font-medium transition-colors ${
                  mainView === "home"
                    ? "bg-background shadow-sm text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                <HomeIcon className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">ホーム</span>
              </button>
              <button
                onClick={() => setMainView("library")}
                title="ライブラリ"
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[13px] font-medium transition-colors ${
                  mainView === "library"
                    ? "bg-background shadow-sm text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                <BookImage className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">ライブラリ</span>
              </button>
            </div>
          )}

          {/* Search */}
          <div className="relative flex-1 max-w-2xl">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="作品を検索（品番・タイトル・出演者・メーカー）"
              className="w-full h-10 bg-muted/40 border-none rounded-full pl-10 pr-9
                text-sm focus:outline-none focus:ring-2 focus:ring-primary/40 transition-all"
            />
            {searchQuery && (
              <button
                onClick={() => setSearchQuery("")}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>

          {/* Right controls */}
          {mounted && (
            <div className="flex items-center gap-2 ml-auto flex-none">

              {/* ── View controls group ── */}
              <div className="flex items-center gap-1 bg-muted/40 rounded-xl p-1">
                {/* Density picker */}
                {DENSITY_OPTIONS.map(({ value, icon, label }) => (
                  <button
                    key={value}
                    onClick={() => setDensity(value)}
                    title={label}
                    className={`p-2 rounded-lg transition-colors ${
                      density === value
                        ? "bg-background shadow-sm text-foreground"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    {icon}
                  </button>
                ))}

                <div className="w-px h-5 bg-border/60 mx-0.5" />

                {/* Cover layout */}
                <button
                  onClick={() => setCoverLayout("portrait")}
                  title="縦長カバー"
                  className={`p-2 rounded-lg transition-colors ${
                    coverLayout === "portrait"
                      ? "bg-background shadow-sm text-foreground"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  <RectangleVertical className="w-4 h-4" />
                </button>
                <button
                  onClick={() => setCoverLayout("landscape")}
                  title="横長カバー"
                  className={`p-2 rounded-lg transition-colors ${
                    coverLayout === "landscape"
                      ? "bg-background shadow-sm text-foreground"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  <RectangleHorizontal className="w-4 h-4" />
                </button>

                <div className="w-px h-5 bg-border/60 mx-0.5" />

                {/* Display fields toggle */}
                <div className="relative">
                  <button
                    onClick={() => setShowDisplayPanel((v) => !v)}
                    title="表示項目"
                    className={`p-2 rounded-lg transition-colors ${
                      showDisplayPanel
                        ? "bg-background shadow-sm text-primary"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    <SlidersHorizontal className="w-4 h-4" />
                  </button>
                  {showDisplayPanel && <DisplaySettingsPanel onClose={closePanel} />}
                </div>
              </div>

              {/* Theme */}
              <div className="flex items-center bg-muted/40 rounded-xl p-1">
                {(["light", "dark", "system"] as const).map((t) => (
                  <button
                    key={t}
                    onClick={() => setTheme(t)}
                    title={t === "light" ? "ライト" : t === "dark" ? "ダーク" : "システム"}
                    className={`p-2 rounded-lg transition-colors ${
                      theme === t
                        ? "bg-background shadow-sm text-foreground"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    {t === "light" ? <Sun className="w-4 h-4" /> : t === "dark" ? <Moon className="w-4 h-4" /> : <Monitor className="w-4 h-4" />}
                  </button>
                ))}
              </div>

              {/* Nav icons */}
              <div className="flex items-center gap-0.5 border-l border-border/50 pl-2">
                <NavIcon href="/import"      icon={<HardDriveUpload className="w-4.5 h-4.5 text-blue-400" />}    title="インポート" />
                <NavIcon href="/collections" icon={<FolderHeart className="w-4.5 h-4.5 text-emerald-400" />}   title="コレクション" />
                <NavIcon href="/organize"   icon={<TableProperties className="w-4.5 h-4.5 text-violet-400" />}  title="一括整理" />
                <NavIcon href="/duplicates" icon={<Copy className="w-4.5 h-4.5 text-rose-400" />}              title="重複作品" />
                <NavIcon href="/triage"     icon={<AlertTriangle className="w-4.5 h-4.5 text-amber-400" />}     title="トリアージ" />
                <NavIcon href="/jobs"       icon={<Activity className="w-4.5 h-4.5" />}                         title="ジョブ" />
                <NavIcon href="/history"    icon={<Clock className="w-4.5 h-4.5" />}                            title="履歴" />
                <NavIcon href="/settings"   icon={<Settings2 className="w-4.5 h-4.5" />}                        title="設定" />
              </div>
            </div>
          )}
        </div>
      </header>

      {/* ── MediaType filter tabs + Sort (library mode only) ── */}
      {mainView === "library" && <div className="w-full border-b border-border/40 bg-background/80 backdrop-blur sticky top-16 z-40">
        <div className="container px-4 md:px-6 flex items-center gap-1 h-10">
          {/* MediaType tabs (scrollable) */}
          <div className="flex items-center gap-1 overflow-x-auto scrollbar-none flex-1 min-w-0">
            {MEDIA_TYPE_TABS.map(({ value, label }) => (
              <button
                key={value ?? "all"}
                onClick={() => setMediaTypeFilter(value)}
                className={`flex-none px-3 py-1.5 rounded-lg text-[13px] font-medium transition-colors
                  ${mediaTypeFilter === value
                    ? "bg-primary/10 text-primary"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted/50"
                  }`}
              >
                {label}
              </button>
            ))}
          </div>

          {/* Sort selector */}
          <div className="flex-none border-l border-border/40 pl-3 ml-1">
            <select
              value={sort}
              onChange={(e) => setSort(e.target.value as SortOption)}
              className="h-7 text-[12px] bg-transparent text-muted-foreground border border-border/50
                rounded-lg px-2 pr-6 cursor-pointer hover:text-foreground hover:border-border
                transition-colors focus:outline-none focus:ring-1 focus:ring-primary/30
                appearance-none"
              style={{ backgroundImage: "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='6' viewBox='0 0 10 6'%3E%3Cpath d='M1 1l4 4 4-4' stroke='%23888' stroke-width='1.5' fill='none' stroke-linecap='round'/%3E%3C/svg%3E\")", backgroundRepeat: "no-repeat", backgroundPosition: "right 6px center" }}
            >
              {SORT_OPTIONS.map(({ value, label }) => (
                <option key={value} value={value}>{label}</option>
              ))}
            </select>
          </div>
        </div>
      </div>}

      {/* Content area */}
      {mainView === "home" ? (
        <DashboardView onSwitchToLibrary={() => setMainView("library")} />
      ) : (
        <div className="flex-1 w-full px-4 md:px-6 py-5">
          <GalleryGrid />
        </div>
      )}

      <JobQueuePanel />
    </main>
  );
}

function NavIcon({ href, icon, title }: { href: string; icon: React.ReactNode; title: string }) {
  return (
    <Link
      href={href}
      title={title}
      className="p-2 rounded-xl text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors"
    >
      {icon}
    </Link>
  );
}
