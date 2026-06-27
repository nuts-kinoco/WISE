"use client";

import { useGalleryStore } from "@/store/useGalleryStore";
import { ArrowLeft, Monitor, Moon, Sun, Settings2, Globe } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";

export default function SettingsPage() {
  const { theme, setTheme } = useGalleryStore();
  const [language, setLanguage] = useState('en');
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  if (!mounted) return null;

  return (
    <main className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mr-6">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-bold text-xl">
            <Settings2 className="w-5 h-5 text-primary" />
            <span>Settings</span>
          </div>
        </div>
      </header>

      <div className="container max-w-3xl mx-auto py-10 px-4 md:px-6">
        <div className="space-y-8">
          
          {/* Theme Settings */}
          <section className="space-y-4">
            <h2 className="text-xl font-semibold border-b pb-2">Appearance</h2>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <button
                onClick={() => setTheme('light')}
                className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'light' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}
              >
                <Sun className="w-8 h-8 mb-3" />
                <span className="font-medium">Light</span>
              </button>
              <button
                onClick={() => setTheme('dark')}
                className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'dark' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}
              >
                <Moon className="w-8 h-8 mb-3" />
                <span className="font-medium">Dark</span>
              </button>
              <button
                onClick={() => setTheme('system')}
                className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'system' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}
              >
                <Monitor className="w-8 h-8 mb-3" />
                <span className="font-medium">System</span>
              </button>
            </div>
          </section>

          {/* Language Settings */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Globe className="w-5 h-5" /> Language
            </h2>
            <div className="bg-card border rounded-xl overflow-hidden">
              <button
                onClick={() => setLanguage('en')}
                className={`w-full flex items-center justify-between p-4 border-b transition-colors hover:bg-muted/50 ${language === 'en' ? 'bg-primary/5' : ''}`}
              >
                <span className="font-medium">English</span>
                {language === 'en' && <span className="w-3 h-3 rounded-full bg-primary"></span>}
              </button>
              <button
                onClick={() => setLanguage('ja')}
                className={`w-full flex items-center justify-between p-4 transition-colors hover:bg-muted/50 ${language === 'ja' ? 'bg-primary/5' : ''}`}
              >
                <span className="font-medium">日本語 (Japanese)</span>
                {language === 'ja' && <span className="w-3 h-3 rounded-full bg-primary"></span>}
              </button>
            </div>
          </section>

        </div>
      </div>
    </main>
  );
}
