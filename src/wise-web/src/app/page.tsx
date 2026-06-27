"use client";

import { useGalleryStore } from "@/store/useGalleryStore";
import { GalleryGrid } from "@/components/gallery/GalleryGrid";
import { Search, Grid, LayoutGrid, Rows3, Moon, Sun, Monitor, Settings2, HardDriveUpload, Clock, Activity } from "lucide-react";
import { useEffect, useState } from "react";
import Link from "next/link";

export default function Home() {
  const { searchQuery, setSearchQuery, cardSize, setCardSize, theme, setTheme } = useGalleryStore();
  
  // Prevent hydration mismatch on toggle buttons
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);

  return (
    <main className="flex min-h-screen flex-col bg-background">
      {/* Header Area */}
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <div className="flex items-center gap-2 font-bold text-xl mr-6 tracking-wider">
            <span className="text-primary">WISE</span>
            <span className="text-muted-foreground font-normal text-sm">Media Library</span>
          </div>

          <div className="flex-1 flex items-center justify-center">
            <div className="relative w-full max-w-xl">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search works by title or identifier..."
                className="w-full h-10 bg-muted/50 border-none rounded-full pl-10 pr-4 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50 transition-all"
              />
            </div>
          </div>

          <div className="flex items-center gap-4 ml-6">
            {mounted && (
              <>
                <div className="flex items-center bg-muted/50 rounded-full p-1">
                  <button onClick={() => setCardSize('sm')} className={`p-1.5 rounded-full ${cardSize === 'sm' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <Grid className="w-4 h-4" />
                  </button>
                  <button onClick={() => setCardSize('md')} className={`p-1.5 rounded-full ${cardSize === 'md' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <LayoutGrid className="w-4 h-4" />
                  </button>
                  <button onClick={() => setCardSize('lg')} className={`p-1.5 rounded-full ${cardSize === 'lg' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <Rows3 className="w-4 h-4" />
                  </button>
                </div>

                <div className="flex items-center bg-muted/50 rounded-full p-1">
                  <button onClick={() => setTheme('light')} className={`p-1.5 rounded-full ${theme === 'light' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <Sun className="w-4 h-4" />
                  </button>
                  <button onClick={() => setTheme('dark')} className={`p-1.5 rounded-full ${theme === 'dark' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <Moon className="w-4 h-4" />
                  </button>
                  <button onClick={() => setTheme('system')} className={`p-1.5 rounded-full ${theme === 'system' ? 'bg-background shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}>
                    <Monitor className="w-4 h-4" />
                  </button>
                </div>

                <div className="flex items-center gap-1 border-l pl-4 ml-2">
                  <Link href="/history" className="p-2 rounded-full text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors" title="Event History">
                    <Clock className="w-5 h-5" />
                  </Link>
                  <Link href="/jobs" className="p-2 rounded-full text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors" title="Background Jobs">
                    <Activity className="w-5 h-5" />
                  </Link>
                  <Link href="/import" className="p-2 rounded-full text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors" title="Import Pipeline">
                    <HardDriveUpload className="w-5 h-5 text-blue-500" />
                  </Link>
                  <Link href="/settings" className="p-2 rounded-full text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors" title="Settings">
                    <Settings2 className="w-5 h-5" />
                  </Link>
                </div>
              </>
            )}
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <div className="flex-1 w-full px-4 md:px-6 py-6">
        <GalleryGrid />
      </div>
    </main>
  );
}
