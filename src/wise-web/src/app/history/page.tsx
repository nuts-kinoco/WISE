"use client";

import { useState, useEffect } from "react";
import { ArrowLeft, Activity, Image as ImageIcon, Database, FolderPlus, Loader2 } from "lucide-react";
import Link from "next/link";
import { API_BASE_URL } from "@/lib/api";

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

export default function HistoryPage() {
  const [historyEvents, setHistoryEvents] = useState<HistoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    fetchHistory();
  }, []);

  const fetchHistory = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/history`);
      if (res.ok) {
        setHistoryEvents(await res.json());
      }
    } catch (err) {
      console.error("Failed to fetch history", err);
    } finally {
      setIsLoading(false);
    }
  };

  const getEventIcon = (eventType: string) => {
    const type = eventType.toLowerCase();
    if (type.includes("asset")) return <ImageIcon className="w-5 h-5 text-indigo-400" />;
    if (type.includes("work")) return <FolderPlus className="w-5 h-5 text-emerald-400" />;
    if (type.includes("metadata")) return <Database className="w-5 h-5 text-blue-400" />;
    return <Activity className="w-5 h-5 text-muted-foreground" />;
  };

  return (
    <main className="min-h-screen bg-background text-foreground pb-20">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mr-6">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-bold text-xl">
            <Activity className="w-5 h-5 text-primary" />
            <span>System History</span>
          </div>
        </div>
      </header>

      <div className="container max-w-4xl mx-auto py-8 px-4 md:px-6">
        {isLoading ? (
          <div className="flex justify-center py-20">
            <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
          </div>
        ) : historyEvents.length === 0 ? (
          <div className="text-center py-20 border border-dashed rounded-2xl text-muted-foreground">
            No history events found.
          </div>
        ) : (
          <div className="relative border-l border-muted ml-4 space-y-8 pb-10">
            {historyEvents.map((event) => (
              <div key={event.id} className="relative pl-8">
                {/* Timeline dot */}
                <div className="absolute -left-3.5 top-1 w-7 h-7 bg-background border-2 border-muted rounded-full flex items-center justify-center">
                  <div className="w-2.5 h-2.5 bg-primary/50 rounded-full" />
                </div>
                
                <div className="bg-card border rounded-xl p-4 shadow-sm hover:shadow-md transition-shadow">
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex items-center gap-2 font-bold text-sm">
                      {getEventIcon(event.eventType)}
                      {event.eventType}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {new Date(event.occurredAt).toLocaleString()}
                    </div>
                  </div>
                  
                  <div className="text-sm mb-3">
                    {event.payload || "No details provided."}
                  </div>
                  
                  <div className="flex items-center justify-between text-xs text-muted-foreground border-t pt-3 mt-3">
                    <div className="flex items-center gap-3">
                      <span>Actor: {event.actor}</span>
                      <span>Source: {event.source}</span>
                    </div>
                    {event.targetId && event.targetIdentifier && (
                      <Link 
                        href={`/gallery/${event.targetId}`}
                        className="text-primary hover:underline font-medium"
                      >
                        View {event.targetIdentifier}
                      </Link>
                    )}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}

